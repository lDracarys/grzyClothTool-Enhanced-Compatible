using CodeWalker;
using CodeWalker.GameFiles;
using grzyClothTool.Controls;
using grzyClothTool.Models.Drawable;
using grzyClothTool.Models.Texture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace grzyClothTool.Helpers;
public static class CWHelper
{
    public static PreviewWindowHost DockedPreviewHost;
    public static string GTAVPath => GTAFolder.GetCurrentGTAFolderWithTrailingSlash();

    private static Enums.SexType? PrevDrawableSex;

    public static void Init()
    {
        var isFolderValid = GTAFolder.IsCurrentGTAFolderValid();
        if (!isFolderValid)
        {
            // Try the path we persisted ourselves (survives across single-file publishes - see
            // PersistentSettingsHelper.GtaFolder) before falling back to auto-detection. This is what
            // lets a previously-configured GTA folder keep working after rebuilding/republishing GCT.
            var savedFolder = PersistentSettingsHelper.Instance.GtaFolder;
            if (!string.IsNullOrEmpty(savedFolder) && GTAFolder.ValidateGTAFolder(savedFolder))
            {
                GTAFolder.SetGTAFolder(savedFolder);
                isFolderValid = true;
            }
        }

        if (!isFolderValid)
        {
            var folder = GTAFolder.AutoDetectFolder();
            if (folder != null)
            {
                SetGTAFolder(folder);
            }
        }
    }

    public static bool SetGTAFolder(string path)
    {
        var success = GTAFolder.SetGTAFolder(path);
        if (success)
        {
            PersistentSettingsHelper.Instance.GtaFolder = path;
        }
        return success;
    }


    public static bool IsGTAFolderValid() => GTAFolder.IsCurrentGTAFolderValid();

    // Resource versions used by CodeWalker to distinguish Enhanced (gen9) from Legacy (gen8) files.
    // See CodeWalker.GameFiles.YddFile.GetVersion / YtdFile.GetVersion.
    private const int YddGen9Version = 159;
    private const int YtdGen9Version = 5;

    /// <summary>
    /// Reads the RSC7 header of a resource file (magic + version, first 8 bytes) directly from
    /// disk to determine whether it is already in Enhanced (gen9) format, without fully loading it.
    /// Returns false if the file doesn't exist, is too short, or isn't a valid RSC7 resource
    /// (e.g. an encrypted/placeholder drawable) - callers already handle those cases separately.
    /// </summary>
    private static bool DetectIsGen9(string filePath, int gen9Version)
    {
        const uint MagicRsc7 = 0x37435352;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
            Span<byte> buffer = stackalloc byte[8];
            if (fs.Read(buffer) < 8) return false;

            uint magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(0, 4));
            if (magic != MagicRsc7) return false;

            int version = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(4, 4));
            return version == gen9Version;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsYddGen9(string filePath) => DetectIsGen9(filePath, YddGen9Version);
    public static bool IsYtdGen9(string filePath) => DetectIsGen9(filePath, YtdGen9Version);

    /// <summary>
    /// Sets RpfManager.IsGen9 based on a .ydd file's own RSC7 header before loading it, restoring
    /// the previous value when disposed. Every place that calls YddFile.Load/LoadAsync directly
    /// (rather than through CreateYddFile) must wrap the call with this - the resource reader has
    /// no way to detect gen9 on its own, it just reads the static RpfManager.IsGen9 flag.
    /// Usage: using (CWHelper.ScopedYddGen9(path)) { await yddFile.LoadAsync(bytes); }
    /// </summary>
    public static IDisposable ScopedYddGen9(string filePath) => new ScopedIsGen9(IsYddGen9(filePath));

    /// <summary>Same as <see cref="ScopedYddGen9"/>, for .ytd files.</summary>
    public static IDisposable ScopedYtdGen9(string filePath) => new ScopedIsGen9(IsYtdGen9(filePath));

    /// <summary>
    /// Same locking as ScopedYddGen9/ScopedYtdGen9, but forces RpfManager.IsGen9 to a specific value
    /// instead of detecting it from a file. Used by Gen9Converter call sites (which always need
    /// IsGen9 = true while converting) so they participate in the same serialization.
    /// Usage: using (CWHelper.ScopedGen9(true)) { ... }
    /// </summary>
    public static IDisposable ScopedGen9(bool isGen9) => new ScopedIsGen9(isGen9);

    // RpfManager.IsGen9 is a single global static flag with no per-call/per-thread context.
    // ScopedIsGen9 is used from parallel build/preview code paths (e.g. BuildResourceHelper's
    // BatchResaveYdd runs several ResaveYdd calls concurrently), so setting/reading/restoring the
    // flag must be serialized - otherwise one thread's file load can be corrupted by another
    // thread flipping the flag mid-read, causing spurious "illegal position!" resource parse errors.
    private static readonly object _gen9FlagLock = new();

    private sealed class ScopedIsGen9 : IDisposable
    {
        private readonly bool _previous;
        public ScopedIsGen9(bool isGen9)
        {
            Monitor.Enter(_gen9FlagLock);
            _previous = RpfManager.IsGen9;
            RpfManager.IsGen9 = isGen9;
        }
        public void Dispose()
        {
            RpfManager.IsGen9 = _previous;
            Monitor.Exit(_gen9FlagLock);
        }
    }

    public static string GetGTAFolderInvalidReason()
    {
        GTAFolder.ValidateGTAFolder(GTAFolder.CurrentGTAFolder, out var reason);
        return $"{reason} (path: '{GTAFolder.CurrentGTAFolder}')";
    }

    public static bool TryRecoverGTAFolder()
    {
        if (GTAFolder.IsCurrentGTAFolderValid())
        {
            return true;
        }

        var savedFolder = PersistentSettingsHelper.Instance.GtaFolder;
        if (!string.IsNullOrEmpty(savedFolder) && GTAFolder.ValidateGTAFolder(savedFolder))
        {
            GTAFolder.SetGTAFolder(savedFolder);
            return true;
        }

        var folder = GTAFolder.AutoDetectFolder();
        if (folder != null)
        {
            SetGTAFolder(folder);
        }

        return GTAFolder.IsCurrentGTAFolderValid();
    }

    public static YtdFile GetYtdFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"YTD file not found: '{path}'. It may have been moved, renamed, or deleted.",
                path);
        }

        var ytdFile = new YtdFile();
        // Same gen9 detection/locking as everywhere else (see ScopedYddGen9/ScopedYtdGen9 above) -
        // this is the path ImgHelper.GetImage() uses for .ytd thumbnails, so without it, gen9 .ytd
        // files fail to load (silently, from the thumbnail generator's point of view) or parse as
        // corrupted data.
        using (ScopedYtdGen9(path))
        {
            ytdFile.Load(File.ReadAllBytes(path));
        }
        return ytdFile;
    }

    public static YtdFile CreateYtdFile(GTexture texture, string name)
    {
        // Give a clear, actionable error when the source file is gone (common for external
        // projects when the user moves/renames/deletes the original image). Without this the
        // caller only sees a cryptic native ImageMagick "unable to open" error.
        if (!File.Exists(texture.FullFilePath))
        {
            throw new FileNotFoundException(
                $"Texture file not found: '{texture.FullFilePath}'. It may have been moved, renamed, or deleted.",
                texture.FullFilePath);
        }

        byte[] data = texture.Extension switch
        {
            ".ytd" => File.ReadAllBytes(texture.FullFilePath), // Read existing YTD file directly
            ".png" or ".jpg" or ".dds" => ImgHelper.GetDDSBytes(texture), // Create DDS texture
            _ => throw new NotSupportedException($"Unsupported file extension: {texture.Extension}"),
        };

        // A source .ytd may already be Enhanced (gen9) format; freshly-built DDS textures are
        // always legacy. Detect from disk so the resource reader parses the correct layout -
        // RpfManager.IsGen9 isn't derived automatically from the file's own header.
        var isGen9 = texture.Extension == ".ytd" && IsYtdGen9(texture.FullFilePath);
        YtdFile ytd;
        using (new ScopedIsGen9(isGen9))
        {
            RpfFileEntry rpf = RpfFile.CreateResourceFileEntry(ref data, 0);
            var decompressedData = ResourceBuilder.Decompress(data);
            ytd = RpfFile.GetFile<YtdFile>(rpf, decompressedData);
        }
        ytd.Name = Path.GetFileNameWithoutExtension(name);

        return ytd;
    }

    public static YddFile CreateYddFile(GDrawable d)
    {
        try
        {
            byte[] data = File.ReadAllBytes(d.FullFilePath);

            YddFile ydd;
            // See CreateYtdFile above - detect gen9 from the file's own RSC7 header before parsing.
            using (new ScopedIsGen9(IsYddGen9(d.FullFilePath)))
            {
                RpfFileEntry rpf = RpfFile.CreateResourceFileEntry(ref data, 0);
                var decompressedData = ResourceBuilder.Decompress(data);
                ydd = RpfFile.GetFile<YddFile>(rpf, decompressedData);
            }
            var drawable = ydd.Drawables.First();
            drawable.Name = Path.GetFileNameWithoutExtension(d.Name);

            drawable.IsHairScaleEnabled = d.EnableHairScale;
            if (drawable.IsHairScaleEnabled)
            {
                drawable.HairScaleValue = d.HairScaleValue;
            }

            drawable.IsHighHeelsEnabled = d.EnableHighHeels;
            if (drawable.IsHighHeelsEnabled)
            {
                drawable.HighHeelsValue = d.HighHeelsValue / 10;
            }

            return ydd;
        }
        catch (Exception ex)
        {
            TelemetryHelper.CaptureExceptionWithAttachment(ex, d.FullFilePath);
            throw;
        }
    }

    public static void SetPedModel(Enums.SexType sexType)
    {
        string pedModel = sexType == Enums.SexType.male ? "mp_m_freemode_01" : "mp_f_freemode_01";
        PrevDrawableSex = sexType;
        DockedPreviewHost?.SetPedModel(pedModel);
    }

    public static void SendDrawableUpdateToPreview(EventArgs args)
    {
        if (DockedPreviewHost == null)
        {
            return;
        }

        var selectedDrawables = MainWindow.AddonManager.SelectedAddon.SelectedDrawables;

        // Don't send anything if no drawables are selected
        if (selectedDrawables.Count == 0) return;

        Dictionary<string, string> updateDict = [];
        if (args is DrawableUpdatedArgs dargs)
        {
            updateDict[dargs.UpdatedName] = dargs.Value.ToString();
        }

        if (selectedDrawables.Count == 1)
        {
            var firstSelected = selectedDrawables.First();
            if (PrevDrawableSex != firstSelected.Sex)
            {
                SetPedModel(firstSelected.Sex);
                updateDict.Add("GenderChanged", "");
            }
        }

        DockedPreviewHost.UpdateDrawables(selectedDrawables, MainWindow.AddonManager.SelectedAddon.SelectedTexture, updateDict);
    }

    public static void OpenDrawableInPreview(GDrawable drawable)
    {
        if (drawable == null)
            return;

        if (!MainWindow.AddonManager.SelectedAddon.SelectedDrawables.Contains(drawable))
        {
            MainWindow.AddonManager.SelectedAddon.SelectedDrawables.Clear();
            MainWindow.AddonManager.SelectedAddon.SelectedDrawables.Add(drawable);
            MainWindow.AddonManager.SelectedAddon.SelectedDrawable = drawable;
        }

        var mainWindow = MainWindow.Instance;
        if (mainWindow == null) return;

        if (mainWindow.PreviewAnchorable != null)
        {
            mainWindow.PreviewAnchorable.Show();
            mainWindow.PreviewHost?.InitializePreview();

            if (!drawable.IsEncrypted)
            {
                SendDrawableUpdateToPreview(new RoutedEventArgs());
            }

            MainWindow.AddonManager.IsPreviewEnabled = true;
        }
    }
}
