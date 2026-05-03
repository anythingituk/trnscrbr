using System.IO;
using System.Net.Http;

namespace Trnscrbr.Services;

public static class LocalSetupErrorFormatter
{
    public static string Format(string prefix, Exception exception)
    {
        return $"{prefix}: {GetUserMessage(exception)}";
    }

    public static string GetUserMessage(Exception exception)
    {
        var message = exception.Message;

        if (exception is HttpRequestException)
        {
            return "Network request failed. Check your internet connection and try again.";
        }

        if (exception is UnauthorizedAccessException)
        {
            return "Windows blocked access to a required file or folder. Try running setup again, or choose a folder your user account can write to.";
        }

        if (exception is IOException ioException)
        {
            if (IsDiskSpaceError(ioException))
            {
                return "There is not enough free disk space for the local dictation download.";
            }

            return "A local file could not be read or written. Close any app using the file and try again.";
        }

        if (exception is InvalidOperationException)
        {
            if (message.Contains("checksum", StringComparison.OrdinalIgnoreCase))
            {
                return "The download did not pass verification. Trnscrbr deleted the bad file; try downloading again.";
            }

            if (message.Contains("whisper-cli.exe", StringComparison.OrdinalIgnoreCase)
                || message.Contains("archive", StringComparison.OrdinalIgnoreCase))
            {
                return "The downloaded local engine package did not contain the expected Windows app file. Try again later or browse to an existing local engine executable.";
            }

            if (message.Contains("exit code", StringComparison.OrdinalIgnoreCase))
            {
                return "The local engine started but failed. Check that the selected model matches the local engine and that the required files are in the same folder.";
            }
        }

        if (exception is TaskCanceledException or TimeoutException)
        {
            return "The operation timed out. Try again, or choose a smaller model on slower hardware.";
        }

        return message;
    }

    private static bool IsDiskSpaceError(IOException exception)
    {
        const int errorHandleDiskFull = unchecked((int)0x80070027);
        const int errorDiskFull = unchecked((int)0x80070070);
        return exception.HResult is errorHandleDiskFull or errorDiskFull;
    }
}
