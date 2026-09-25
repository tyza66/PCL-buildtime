namespace PCL.Avalonia.Services;

public interface ISettingsService
{
    AppSettings Load();

    void Save(AppSettings settings);
}
