﻿using System;
using System.ComponentModel;

namespace UWP_App.Models
{
    public class ScienceModuleData : INotifyPropertyChanged
    {
        private DateTime timeRead;
        private bool readSuccess;
        private float temp;
        private float ph;
        private float salinity;
        private float orp;
        private bool converttoF = true;


        public float Temp
        {
            get
            {
                return temp;
            }
            set
            {
                if (value != temp)
                {
                    temp = value;
                    NotifyPropertyChanged("Temp");
                }
            }
        }

        public float FTemp
        {
            get
            {
                return (temp * 9) / 5 + 32;
            }
        }


        public bool ReadSuccess { get => readSuccess; set => readSuccess = value; }
        public DateTime TimeRead { get => timeRead; set => timeRead = value; }
        public float PH
        {
            get
            {
                return ph;
            }
            set
            {
                if (value != ph)
                {
                    ph = value;
                    NotifyPropertyChanged("PH");
                }
            }
        }

        public float Salinity { get => salinity; set => salinity = value; }
        public float ORP { get => orp; set => orp = value; }
        public bool ConverttoF { get => converttoF; set => converttoF = value; }

        private void NotifyPropertyChanged(string info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
