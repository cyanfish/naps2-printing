using System.Runtime.InteropServices;

namespace NAPS2.Windows.Printing.Interop;

/// <summary>
/// Finds print jobs with the print spooler APIs (winspool.h).
/// </summary>
internal static class Spooler
{
    private const int PRINTER_ENUM_LOCAL = 2;
    private const int PRINTER_ENUM_CONNECTIONS = 4;

    /// <summary>
    /// Gets the name of the printer that has the given print job, or null if no printer has it.
    /// </summary>
    public static string? FindPrinterForJob(uint jobId)
    {
        foreach (var printerName in GetPrinterNames())
        {
            if (!OpenPrinter(printerName, out nint printer, 0))
            {
                continue;
            }
            try
            {
                // Job IDs are unique across the spooler's printers, so only the printer with the job finds it
                GetJob(printer, jobId, 1, 0, 0, out int needed);
                if (needed > 0)
                {
                    return printerName;
                }
            }
            finally
            {
                ClosePrinter(printer);
            }
        }
        return null;
    }

    private static List<string> GetPrinterNames()
    {
        const int flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;
        EnumPrinters(flags, null, 4, 0, 0, out int needed, out _);
        if (needed == 0)
        {
            return [];
        }
        nint buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumPrinters(flags, null, 4, buffer, needed, out _, out int count))
            {
                return [];
            }
            var names = new List<string>();
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PRINTER_INFO_4>(buffer + i * Marshal.SizeOf<PRINTER_INFO_4>());
                if (info.pPrinterName != null)
                {
                    names.Add(info.pPrinterName);
                }
            }
            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_INFO_4
    {
        public string? pPrinterName;
        public string? pServerName;
        public int Attributes;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(int flags, string? name, int level, nint printerEnum, int bufferSize,
        out int bytesNeeded, out int count);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName, out nint printer, nint defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(nint printer);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetJob(nint printer, uint jobId, int level, nint job, int bufferSize, out int bytesNeeded);
}
