namespace DymolaInterface;

public class DymolaSettings
{
    public string DymolaPath { get; set; } = string.Empty;
    public string HostAddress { get; } = "127.0.0.1";
    public int PortNumber { get; set; } = 8082;

    public DymolaSettings()
    {
        if (string.IsNullOrEmpty(DymolaPath)) {
            //Search for the most recent Dymola version
            var year = DateTime.Now.Year + 1;
            var refreshVersionNext = false;
            string versionName;
            while (year > 2020)
            {
                if (refreshVersionNext) 
                {
                    year--;
                    versionName = $"Dymola {year}x Refresh 1";
                }
                else
                    versionName = $"Dymola {year}x";
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), versionName, "bin64", "dymola.exe");
                if (File.Exists(path)) 
                {
                    DymolaPath = path;
                    break;
                }
                refreshVersionNext = !refreshVersionNext;
            }            
        }
    }
}