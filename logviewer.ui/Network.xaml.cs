﻿using System.Windows;
using System.Windows.Input;
using logviewer.logic.ui.network;

namespace logviewer.ui
{
    /// <summary>
    ///     Interaction logic for Network.xaml
    /// </summary>
    public partial class Network
    {
        private readonly NetworkSettingsController controller;

        public Network()
        {
            this.InitializeComponent();
            var model = new NetworkSettingsModel();
            model.PasswordUpdated += this.ModelOnPasswordUpdated;
            this.DataContext = model;
            this.controller = new NetworkSettingsController(model, MainViewModel.Current.SettingsProvider.OptionsProvider);
            this.controller.Initialize();
        }

        private void ModelOnPasswordUpdated(object sender, string s)
        {
            this.Password.Password = s;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.controller.Write(this.Password.Password);
            this.Close();
        }

        private void OnChange(object sender, KeyEventArgs e)
        {
            this.controller.InvokeSettingsChange();
        }
    }
}