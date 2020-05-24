﻿using System.Windows.Input;

namespace YAPA.Shared.Contracts
{
    public interface IMainViewModel
    {
        IPomodoroEngine Engine { get; set; }
        ICommand StopCommand { get; set; }
        ICommand StartCommand { get; set; }
        ICommand ResetCommand { get; set; }
        ICommand PauseCommand { get; set; }
        ICommand ShowSettingsCommand { get; set; }
        ICommand MinimizeCommand { get; set; }
        ICommand SkipCommand { get; set; }
    }
}
