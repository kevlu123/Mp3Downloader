using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Provider;

namespace Mp3Downloader;

[Activity(
    //Theme = "@style/Maui.SplashTheme",
    Theme = "@style/Maui.MainTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density
    )]
public class MainActivity : MauiAppCompatActivity{
    public static MainActivity Instance { get; private set; }

    public MainActivity() {
        Instance = this;
    }

    public void RequestStoragePermission() {
        bool hasPermission = true;
        const string PERM_CHECK_FILE = "/storage/emulated/0/Music/._perm-check";
        try {
            using var _ = File.Create(PERM_CHECK_FILE);
        } catch (UnauthorizedAccessException) {
            hasPermission = false;
        } finally {
            try {
                File.Delete(PERM_CHECK_FILE);
            } catch (UnauthorizedAccessException) { }
        }

        if (!hasPermission) {
            var uri = Android.Net.Uri.Parse("package:com.kev.Mp3Downloader");
            StartActivity(new Intent(Settings.ActionManageAppAllFilesAccessPermission, uri));
        }
    }
}
