﻿using FluentScheduler;
using UWP_App.Models;

namespace UWP_App.Scheduling
{
    public class OutletOnOffJob : IJob
    {
        private Outlet _outlet;
        private OutletState _newState;

        public OutletOnOffJob(Outlet outlet, OutletState state)
        {
            _outlet = outlet;
            _newState = state;
        }

        public void Execute()
        {
            _outlet.SetState(_newState);
        }
    }
}
