using System.Windows;
using DshDesktop.Settings;

namespace DshDesktop;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var s = App.Settings;
        PortBox.Text = s.BackendPort.ToString();
        CommandBox.Text = s.DshCommand;
        StopBackendBox.IsChecked = s.StopSpawnedBackendOnExit;
        CloseToTrayBox.IsChecked = s.CloseToTray;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            HintBlock.Text = "端口必须是 1-65535 的整数";
            return;
        }

        var s = App.Settings;
        s.BackendPort = port;
        s.DshCommand = CommandBox.Text.Trim();
        s.StopSpawnedBackendOnExit = StopBackendBox.IsChecked == true;
        s.CloseToTray = CloseToTrayBox.IsChecked == true;
        App.SettingsStore.Save(s);
        App.Log.Info("设置已保存");
        HintBlock.Foreground = System.Windows.Media.Brushes.Green;
        HintBlock.Text = "已保存。端口/命令变更在下次「重试」时生效。";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
