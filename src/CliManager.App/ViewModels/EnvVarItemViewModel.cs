using CommunityToolkit.Mvvm.ComponentModel;

namespace CliManager.App.ViewModels;

/// <summary>
/// 环境变量键值对模型。
/// </summary>
public partial class EnvVarItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    public EnvVarItemViewModel() { }

    public EnvVarItemViewModel(string key, string value)
    {
        _key = key;
        _value = value;
    }
}
