using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Serval.Agent;

/// <summary>Minimal, noninteractive Linux-PAM wrapper for the fixed Serval service.</summary>
public static partial class LinuxPam
{
    private const int PamSuccess = 0;
    private const int PamConversationError = 19;
    private const int PamPromptEchoOff = 1;
    private const int PamErrorMessage = 3;
    private const int PamTextInfo = 4;
    private const int PamUserItem = 2;

    public static bool Authenticate(string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        if (!OperatingSystem.IsLinux() || username.Contains('\0') || password.Contains('\0'))
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Run(username, bytes, authenticate: true, configurationDirectory: null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static bool CheckAccount(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return OperatingSystem.IsLinux() && !username.Contains('\0') &&
            Run(username, null, authenticate: false, configurationDirectory: null);
    }

    internal static bool AuthenticateWithConfiguration(
        string username, string password, string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Run(username, bytes, authenticate: true, configurationDirectory);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool Run(
        string username, byte[]? password, bool authenticate, string? configurationDirectory)
    {
        var state = new ConversationState(password);
        var stateHandle = GCHandle.Alloc(state);
        PamConversation callback = Converse;
        var conversation = new PamConv(
            Marshal.GetFunctionPointerForDelegate(callback), GCHandle.ToIntPtr(stateHandle));
        IntPtr handle = IntPtr.Zero;
        var status = PamConversationError;
        try
        {
            status = configurationDirectory is null
                ? PamStart(AgentConfiguration.PamService, username, in conversation, out handle)
                : PamStartConfdir(AgentConfiguration.PamService, username,
                    in conversation, configurationDirectory, out handle);
            if (status != PamSuccess || handle == IntPtr.Zero)
            {
                return false;
            }

            if (authenticate)
            {
                status = PamAuthenticate(handle, 0);
                if (status != PamSuccess)
                {
                    return false;
                }
            }

            status = PamAccountManagement(handle, 0);
            if (status != PamSuccess || PamGetItem(handle, PamUserItem, out var userPointer) != PamSuccess)
            {
                return false;
            }

            return string.Equals(Marshal.PtrToStringUTF8(userPointer), username, StringComparison.Ordinal);
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                _ = PamEnd(handle, status);
            }

            stateHandle.Free();
            GC.KeepAlive(callback);
        }
    }

    private static unsafe int Converse(int count, IntPtr messages, out IntPtr responses, IntPtr appData)
    {
        responses = IntPtr.Zero;
        if (count is < 1 or > 16 || messages == IntPtr.Zero)
        {
            return PamConversationError;
        }

        var state = (ConversationState)GCHandle.FromIntPtr(appData).Target!;
        var allocated = Calloc((nuint)count, (nuint)Marshal.SizeOf<PamResponse>());
        if (allocated == IntPtr.Zero)
        {
            return PamConversationError;
        }

        try
        {
            for (var index = 0; index < count; index++)
            {
                var message = Marshal.ReadIntPtr(messages, index * IntPtr.Size);
                if (message == IntPtr.Zero)
                {
                    return PamConversationError;
                }

                var style = Marshal.ReadInt32(message);
                if (style is PamErrorMessage or PamTextInfo)
                {
                    continue;
                }

                if (style != PamPromptEchoOff || state.Password is null || state.PromptAnswered)
                {
                    return PamConversationError;
                }

                state.PromptAnswered = true;
                var response = Malloc((nuint)(state.Password.Length + 1));
                if (response == IntPtr.Zero)
                {
                    return PamConversationError;
                }

                Marshal.Copy(state.Password, 0, response, state.Password.Length);
                Marshal.WriteByte(response, state.Password.Length, 0);
                Marshal.WriteIntPtr(allocated, index * Marshal.SizeOf<PamResponse>(), response);
            }

            responses = allocated;
            return PamSuccess;
        }
        catch
        {
            return PamConversationError;
        }
        finally
        {
            if (responses == IntPtr.Zero)
            {
                for (var index = 0; index < count; index++)
                {
                    var response = Marshal.ReadIntPtr(allocated, index * Marshal.SizeOf<PamResponse>());
                    if (response != IntPtr.Zero)
                    {
                        new Span<byte>((void*)response, state.Password?.Length ?? 0).Clear();
                        Free(response);
                    }
                }

                Free(allocated);
            }
        }
    }

    private sealed class ConversationState(byte[]? password)
    {
        internal byte[]? Password { get; } = password;

        internal bool PromptAnswered { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PamConv(IntPtr callback, IntPtr appData)
    {
        private readonly IntPtr callback = callback;
        private readonly IntPtr appData = appData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PamResponse
    {
        private readonly IntPtr response;
        private readonly int returnCode;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PamConversation(int count, IntPtr messages, out IntPtr responses, IntPtr appData);

    [LibraryImport("libpam.so.0", EntryPoint = "pam_start", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PamStart(string service, string user, in PamConv conversation, out IntPtr handle);

    [LibraryImport("libpam.so.0", EntryPoint = "pam_start_confdir", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PamStartConfdir(
        string service, string user, in PamConv conversation, string configurationDirectory, out IntPtr handle);

    [LibraryImport("libpam.so.0", EntryPoint = "pam_authenticate")]
    private static partial int PamAuthenticate(IntPtr handle, int flags);

    [LibraryImport("libpam.so.0", EntryPoint = "pam_acct_mgmt")]
    private static partial int PamAccountManagement(IntPtr handle, int flags);

    [LibraryImport("libpam.so.0", EntryPoint = "pam_get_item")]
    private static partial int PamGetItem(IntPtr handle, int itemType, out IntPtr item);

    [LibraryImport("libpam.so.0", EntryPoint = "pam_end")]
    private static partial int PamEnd(IntPtr handle, int status);

    [LibraryImport("libc", EntryPoint = "malloc")]
    private static partial IntPtr Malloc(nuint size);

    [LibraryImport("libc", EntryPoint = "calloc")]
    private static partial IntPtr Calloc(nuint count, nuint size);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(IntPtr pointer);
}
