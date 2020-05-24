using System;
using System.Windows;
using YAPA.Shared.Contracts;

namespace YAPA.WPF.Specifics
{
    public class MinimizeCommand : IMinimizeCommand
    {
        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            if (Application.Current.MainWindow != null)
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
        }

        public event EventHandler CanExecuteChanged;
    }
}
