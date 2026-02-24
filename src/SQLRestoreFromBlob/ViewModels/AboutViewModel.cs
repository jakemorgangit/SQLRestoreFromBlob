namespace SQLRestoreFromBlob.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    public string AppName => "SQL Restore From Blob";
    public string Version => "v1.0.0";
    public string Year => "2026";
    public string Author => "Jake Morgan";
    public string Company => "Blackcat Data Solutions Ltd";
    public string Website => "https://blackcat.wales";
    public string Description => "A production-ready utility for restoring SQL Server databases from Azure Blob Storage backups with full support for point-in-time recovery using Full, Differential, and Transaction Log backup chains.";
}
