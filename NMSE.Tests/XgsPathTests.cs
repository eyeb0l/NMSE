using NMSE.IO;

namespace NMSE.Tests;

public sealed class XgsPathTests
{
    [Theory]
    [InlineData("../Slot1Auto")]
    [InlineData("..\\Slot1Auto")]
    [InlineData("/Slot1Auto")]
    [InlineData("Slot1Auto/../outside")]
    public void SlotDirectory_RejectsBothPlatformsTraversal(string identifier)
    {
        Assert.Throws<ArgumentException>(() => XgsSaveManager.GetSlotDirectory(Path.GetTempPath(), identifier));
    }

    [Fact]
    public void Enumeration_PreservesNativePathAndMixedCaseIdentifier()
    {
        string root = Path.Combine(Path.GetTempPath(), "NMSE XGS " + Guid.NewGuid().ToString("N"));
        try
        {
            string directory = XgsSaveManager.GetSlotDirectory(root, "sLoT1mAnUaL");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "data"), "{\"PlayerStateData\":{}}");
            var slot = Assert.Single(XgsSaveManager.EnumerateSlots(root));
            Assert.Equal("sLoT1mAnUaL", slot.Key);
            Assert.Equal(Path.Combine(directory, "data"), slot.Value.DataFilePath);
            Assert.Equal(SaveFileManager.Platform.XboxGamePass, SaveFileManager.DetectPlatform(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void EnumerationAndWrites_RejectLinkedSlotsAndPayloads()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "NMSE-xgs-links-" + Guid.NewGuid().ToString("N"));
        string outside = root + "-outside";
        try
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "data"), "{\"Outside\":true}");
            Directory.CreateSymbolicLink(Path.Combine(root, "Slot1Auto"), outside);
            Assert.Empty(XgsSaveManager.EnumerateSlots(root));
            Assert.Throws<IOException>(() => XgsSaveManager.GetSlotDirectory(root, "Slot1Auto"));
            Directory.CreateDirectory(Path.Combine(root, "Slot2Auto"));
            File.CreateSymbolicLink(Path.Combine(root, "Slot2Auto", "data"), Path.Combine(outside, "data"));
            Assert.Empty(XgsSaveManager.EnumerateSlots(root));
            Assert.False(XgsSaveManager.IsXgsContainerDirectory(root));
            Assert.Contains("Outside", File.ReadAllText(Path.Combine(outside, "data")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }
}
