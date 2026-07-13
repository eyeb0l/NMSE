using System.Text;
using NMSE.IO;
using NMSE.Models;

namespace NMSE.Tests;

public sealed class PS4MemoryDatSaveTests
{
    [Fact]
    public void SavePS4MemoryDatSave_SaveWizard_ReplacesOnlySelectedSlot()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nmse-memorydat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "memory.dat");
        try
        {
            byte[] first = Encoding.Latin1.GetBytes("{\"Slot\":1,\"Name\":\"Before\"}\0");
            byte[] second = Encoding.Latin1.GetBytes("{\"Slot\":2,\"Name\":\"Untouched\"}\0");
            var metadata = new Dictionary<int, MemoryDatSlot>
            {
                [1] = NewSaveWizardSlot(1, first.Length),
                [2] = NewSaveWizardSlot(2, second.Length),
            };
            MemoryDatManager.WriteMemoryDatSaveWizard(
                path,
                new Dictionary<int, byte[]> { [1] = first, [2] = second },
                metadata);

            JsonObject replacement = JsonObject.Parse("{\"Slot\":1,\"Name\":\"After\",\"Units\":42}");
            SaveFileManager.SavePS4MemoryDatSave(path, 1, replacement);

            JsonObject? firstReload = SaveFileManager.LoadPS4MemoryDatSave(path, 1);
            JsonObject? secondReload = SaveFileManager.LoadPS4MemoryDatSave(path, 2);
            Assert.Equal("After", firstReload?.GetString("Name"));
            Assert.Equal(42L, firstReload?.GetLong("Units"));
            Assert.Equal("Untouched", secondReload?.GetString("Name"));
            Assert.True(MemoryDatManager.IsSaveWizardFormat(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SavePS4MemoryDatSave_MissingSlot_IsRejected()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"nmse-memorydat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "memory.dat");
        try
        {
            byte[] data = Encoding.Latin1.GetBytes("{\"Slot\":1}\0");
            MemoryDatManager.WriteMemoryDatSaveWizard(
                path,
                new Dictionary<int, byte[]> { [1] = data },
                new Dictionary<int, MemoryDatSlot> { [1] = NewSaveWizardSlot(1, data.Length) });

            Assert.Throws<InvalidOperationException>(() =>
                SaveFileManager.SavePS4MemoryDatSave(path, 2, JsonObject.Parse("{}")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static MemoryDatSlot NewSaveWizardSlot(int index, int length) => new()
    {
        Index = index,
        Exists = true,
        MetaFormat = 1,
        CompressedSize = (uint)length,
        ChunkSize = (uint)length,
        MetaIndex = (uint)index,
        Timestamp = DateTimeOffset.UtcNow,
        DecompressedSize = (uint)length,
        IsSaveWizard = true,
    };
}
