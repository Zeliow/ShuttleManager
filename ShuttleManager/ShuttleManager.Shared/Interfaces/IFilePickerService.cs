namespace ShuttleManager.Shared.Interfaces;

public interface IFilePickerService
{
    Task<PickedFileDto?> PickFileAsync();
}

public class PickedFileDto
{
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public Stream DataStream { get; set; }
    public string ContentType { get; set; }
}