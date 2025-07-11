﻿using System.Configuration;
using System.Data;
using System.Windows;
using TaskMate.ViewModels;

namespace TaskMate;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    
    if (Current.MainWindow is MainWindow window)
    {
        window.DataContext = new MainViewModel();
    }
}
}

