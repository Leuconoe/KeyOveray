using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using KeyOverlay.Widget.Models;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using Windows.Storage;

namespace KeyOverlay.Widget.Services
{
    internal sealed class InputBridgeClient
    {
        private const uint CommandMagic = 0x4B4F564C;
        private const string PipeName = @"\\.\pipe\LOCAL\KeyOverlay.Input";
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private const uint PipeReadModeMessage = 2;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private readonly System.Threading.SemaphoreSlim _launchLock
            = new System.Threading.SemaphoreSlim(1, 1);
        private bool _launchAttempted;

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode,
            SetLastError = true, EntryPoint = "CreateFileFromAppW")]
        private static extern IntPtr CreateFileFromApp(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetNamedPipeHandleState(
            IntPtr namedPipe,
            ref uint mode,
            IntPtr maxCollectionCount,
            IntPtr collectDataTimeout);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TransactNamedPipe(
            IntPtr namedPipe,
            byte[] inputBuffer,
            uint inputBufferSize,
            [Out] byte[] outputBuffer,
            uint outputBufferSize,
            out uint bytesRead,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        public async Task EnsureStartedAsync()
        {
            await _launchLock.WaitAsync();
            try
            {
                if (_launchAttempted)
                {
                    return;
                }

                if (ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0))
                {
                    await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync("InputBridge");
                    await Task.Delay(180);
                }
                _launchAttempted = true;
            }
            finally
            {
                _launchLock.Release();
            }
        }

        public async Task<bool> SendAsync(KeyButtonDefinition key)
        {
            try
            {
                await EnsureStartedAsync();
                if (await SendOnceAsync(key))
                {
                    return true;
                }

                _launchAttempted = false;
                await EnsureStartedAsync();
                return await SendOnceAsync(key);
            }
            catch
            {
                _launchAttempted = false;
                return false;
            }
        }

        private static async Task<bool> SendOnceAsync(KeyButtonDefinition key)
        {
            var command = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(CommandMagic), 0, command, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(key.VirtualKey), 0, command, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((int)key.Modifiers), 0, command, 8, 4);

            var result = await Task.Run(() => SendNative(command));
            await WriteDiagnosticAsync(result);
            return result.Succeeded;
        }

        private static NativeCallResult SendNative(byte[] command)
        {
            var pipe = CreateFileFromApp(
                PipeName,
                GenericRead | GenericWrite,
                0,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);
            if (pipe == InvalidHandle)
            {
                return new NativeCallResult(false, "Open", Marshal.GetLastWin32Error(), 0, 0);
            }

            var response = new byte[4];
            try
            {
                var readMode = PipeReadModeMessage;
                if (!SetNamedPipeHandleState(pipe, ref readMode, IntPtr.Zero, IntPtr.Zero))
                {
                    return new NativeCallResult(false, "Mode", Marshal.GetLastWin32Error(), 0, 0);
                }

                uint bytesRead;
                if (!TransactNamedPipe(
                    pipe,
                    command,
                    (uint)command.Length,
                    response,
                    (uint)response.Length,
                    out bytesRead,
                    IntPtr.Zero))
                {
                    return new NativeCallResult(false, "Transact", Marshal.GetLastWin32Error(),
                        bytesRead, 0);
                }

                var bridgeResult = bytesRead == response.Length
                    ? BitConverter.ToUInt32(response, 0)
                    : 0u;
                return new NativeCallResult(bytesRead == response.Length && bridgeResult == 1,
                    "Reply", 0, bytesRead, bridgeResult);
            }
            finally
            {
                CloseHandle(pipe);
            }
        }

        private static async Task WriteDiagnosticAsync(NativeCallResult result)
        {
            try
            {
                var text = result.Succeeded
                    ? "OK"
                    : string.Format("FAIL Stage={0} Win32={1} Read={2} Bridge=0x{3:X8}",
                        result.Stage, result.Win32Error, result.BytesRead, result.BridgeResult);
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    "input-diagnostic.txt", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, text);
            }
            catch
            {
            }
        }

        private sealed class NativeCallResult
        {
            public NativeCallResult(bool succeeded, string stage, int win32Error,
                uint bytesRead, uint bridgeResult)
            {
                Succeeded = succeeded;
                Stage = stage;
                Win32Error = win32Error;
                BytesRead = bytesRead;
                BridgeResult = bridgeResult;
            }

            public bool Succeeded { get; }
            public string Stage { get; }
            public int Win32Error { get; }
            public uint BytesRead { get; }
            public uint BridgeResult { get; }
        }
    }
}
