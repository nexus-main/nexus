using Microsoft.JSInterop;

namespace Nexus.UI.Services;

partial class NexusJSInterop
{
    public void SaveSetting<T>(string key, T value)
    {
        _commonModule.InvokeVoid("saveSetting", key, value);
    }

    public void ClearSetting(string key)
    {
        _commonModule.InvokeVoid("clearSetting", key);
    }

    public T? LoadSetting<T>(string key)
    {
        return _commonModule.Invoke<T>("loadSetting", key);
    }
}