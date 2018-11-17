﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using UWP_App.Helpers;
using UWP_App.Services;

using Windows.Foundation.Metadata;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using UWP_App.Models;
using Microsoft.Maker.Firmata;
using Microsoft.Maker.RemoteWiring;
using Microsoft.Maker.Serial;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.ApplicationModel.Core;
using Microsoft.Azure.Devices.Client;
using System.Text;
using Windows.Storage;
using FluentScheduler;
using UWP_App.Scheduling;
using Windows.UI.Popups;

namespace UWP_App.Views
{
    // TODO WTS: Change the icons and titles for all NavigationViewItems in ShellPage.xaml.
    public sealed partial class ShellPage : Page, INotifyPropertyChanged
    {
        private NavigationViewItem _selected;
        IStream s_connection;
        RemoteDevice s_arduino;
        UwpFirmata s_firmata;

        IStream p_connection;
        RemoteDevice p_arduino;
        UwpFirmata p_firmata;

        DispatcherTimer getDatatimer;
        DispatcherTimer sendDataTimer;

        internal static ScienceModuleData currentData;

        private DeviceClient client;
        private string IOTHostName = "IOT-Reef.azure-devices.net";
        private string IOTDeviceName = "DevelopmentDevice";
        private string IOTDeviceKey = "GTO6JqpfUNkDSD1JmSM1KYUr4VwwcEU2YJMEifhyFjU=";

        internal static Dictionary<string, Outlet> outletDict; //outlets so we can call them by "name"
        //Dictionary<int, byte> pinNumDict; //physical pin mappings plug number -> pin number this shouldn't change
        //Dictionary<int, string> nameDict; //software plug mappings plug number -> name

        public NavigationViewItem Selected
        {
            get { return _selected; }
            set { Set(ref _selected, value); }
        }

        public ShellPage()
        {
            InitializeComponent();
            HideNavViewBackButton();
            DataContext = this;
            Initialize();
            currentData = new ScienceModuleData();
            outletDict = new Dictionary<string, Outlet>();

            client = DeviceClient.Create(IOTHostName, new DeviceAuthenticationWithRegistrySymmetricKey(IOTDeviceName, IOTDeviceKey), Microsoft.Azure.Devices.Client.TransportType.Http1);

            s_connection = new UsbSerial("VID_2341", "PID_0043");
            s_firmata = new UwpFirmata();
            s_arduino = new RemoteDevice(s_firmata);

            s_firmata.begin(s_connection);
            s_connection.begin(57600, SerialConfig.SERIAL_8N1);

            s_arduino.DeviceReady += SienceModuleReadyAsync;
            s_arduino.DeviceConnectionFailed += ScienceDeviceConnectionFailAsync;
            s_arduino.StringMessageReceived += ScienceDataReceivedAsync;

            p_connection = new UsbSerial("VID_0403", "PID_6001");
            p_firmata = new UwpFirmata();
            p_arduino = new RemoteDevice(p_firmata);

            p_firmata.begin(p_connection);
            p_connection.begin(57600, SerialConfig.SERIAL_8N1);

            p_arduino.DeviceReady += PowerModuleReadyAsync;
            p_arduino.DeviceConnectionFailed += PowerConnectionFailAsync;

            getDatatimer = new DispatcherTimer();
            getDatatimer.Interval = new TimeSpan(0, 0, 5);
            getDatatimer.Tick += getDataTick;
            getDatatimer.Start();

            sendDataTimer = new DispatcherTimer();
            sendDataTimer.Interval = new TimeSpan(0, 0, 30);
            sendDataTimer.Tick += sendDataTickAsync;
            sendDataTimer.Start();
        }

        private void Initialize()
        {
            NavigationService.Frame = shellFrame;
            NavigationService.Navigated += Frame_Navigated;
            KeyboardAccelerators.Add(ActivationService.AltLeftKeyboardAccelerator);
            KeyboardAccelerators.Add(ActivationService.BackKeyboardAccelerator);
        }

        private void Frame_Navigated(object sender, NavigationEventArgs e)
        {
            if (e.SourcePageType == typeof(SettingsPage))
            {
                Selected = navigationView.SettingsItem as NavigationViewItem;
                return;
            }

            Selected = navigationView.MenuItems
                            .OfType<NavigationViewItem>()
                            .FirstOrDefault(menuItem => IsMenuItemForPageType(menuItem, e.SourcePageType));
        }

        private bool IsMenuItemForPageType(NavigationViewItem menuItem, Type sourcePageType)
        {
            var pageType = menuItem.GetValue(NavHelper.NavigateToProperty) as Type;
            return pageType == sourcePageType;
        }

        private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                NavigationService.Navigate(typeof(SettingsPage));
                return;
            }

            var item = navigationView.MenuItems
                            .OfType<NavigationViewItem>()
                            .First(menuItem => (string)menuItem.Content == (string)args.InvokedItem);
            var pageType = item.GetValue(NavHelper.NavigateToProperty) as Type;
            NavigationService.Navigate(pageType);
        }

        private void HideNavViewBackButton()
        {
            if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 6))
            {
                navigationView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T storage, T value, [CallerMemberName]string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void PowerConnectionFailAsync(string message)
        {
            //await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { lblMessages.Text = "Power Module Connection Failed!"; });
        }

        //Ok here's what we're doing. 
        //1. Try and load the outlet information from the settings file
        //2. Problem is on a new debug session it's wiping that file.
        //3. If successful it will load the information on what plugs control what, if not,
        //we load the default file from the assets folder
        //4. Call method which basically is an after-thought constructor, sets the arduino, and the current state
        //that doesn't get called during the DesrializeObject call. (Look into JsonConstructor attibute)
        //5. and Finally, we call the PowerUpRecovery method on the outlet.  This method basically looks at the outlet,
        //determines if there is a schedule, checks the time, and sets the outlet appropiately.

        private async void PowerModuleReadyAsync()
        {
            try
            {
                StorageFolder localFolder = ApplicationData.Current.LocalFolder;
                StorageFile outletfile = await localFolder.GetFileAsync("dictionarysettings.txt");
                string outletState = await FileIO.ReadTextAsync(outletfile);
                outletDict = JsonConvert.DeserializeObject<Dictionary<string, Outlet>>(outletState);

                foreach (var plug in outletDict)
                {
                    plug.Value.AfterDataConst(p_arduino);
                    plug.Value.PowerUpRecovery();
                }
            }
            catch (FileNotFoundException fnfex)
            {
                StorageFolder localFolder = Windows.ApplicationModel.Package.Current.InstalledLocation;
                StorageFile defaultFile = await localFolder.GetFileAsync("Assets\\OutletDefault.txt");
                string outletState = await FileIO.ReadTextAsync(defaultFile);
                outletDict = JsonConvert.DeserializeObject<Dictionary<string, Outlet>>(outletState);
                foreach (var plug in outletDict)
                {
                    plug.Value.AfterDataConst(p_arduino);
                    plug.Value.PowerUpRecovery();
                }
                string serialized = JsonConvert.SerializeObject(outletDict);
                localFolder = ApplicationData.Current.LocalFolder;
                StorageFile file = await localFolder.CreateFileAsync("dictionarysettings.txt", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, serialized);
            }
            catch (Exception ex)
            {
                var msg = new MessageDialog("Unknown Error reading settings files: " + ex.ToString());
                msg.Commands.Add(new UICommand("Close"));
            }

            JobManager.Initialize(new FluentRegistry(outletDict));

        }

        private async void sendDataTickAsync(object sender, object e)
        {
            try
            {
                var serializedData = JsonConvert.SerializeObject(currentData);
                var bytes = Encoding.UTF8.GetBytes(serializedData);
                var message = new Message(bytes);
                await client.SendEventAsync(message);
            }
            catch (Exception ex)
            {
                var msg = new MessageDialog("Unknown Error sending data to cloud: " + ex.ToString());
                msg.Commands.Add(new UICommand("Close"));
            }


        }

        private void getDataTick(object sender, object e)
        {
            byte PH_Query = 0x44;
            s_firmata.sendSysex(PH_Query, new byte[] { }.AsBuffer());
            s_firmata.flush();
        }

        private void ScienceDataReceivedAsync(string message)
        {
            ScienceModuleData deserialized = new ScienceModuleData();

            if (currentData == null)
            {
                currentData = new ScienceModuleData();
            }

            deserialized = JsonConvert.DeserializeObject<ScienceModuleData>(message);
            currentData = deserialized;
            currentData.TimeRead = DateTime.Now;

            foreach (var outlet in outletDict)
            {
                outlet.Value.CheckTriggers(currentData);
            }
        }

        private void ScienceDeviceConnectionFailAsync(string message)
        {
            //await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { lblMessages.Text = "Science Module Connection Failed!"; });
        }

        private void SienceModuleReadyAsync()
        {
            //await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { lblMessages.Text = "Science Module Ready!"; });
        }
    }
}
