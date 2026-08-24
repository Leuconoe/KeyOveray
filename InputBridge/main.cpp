#include <windows.h>
#include <appmodel.h>
#include <sddl.h>
#include <tlhelp32.h>

#include <chrono>
#include <cstdint>
#include <string>
#include <thread>
#include <vector>

namespace
{
    constexpr std::uint32_t CommandMagic = 0x4B4F564C; // KOVL

    constexpr std::int32_t ModifierControl = 1;
    constexpr std::int32_t ModifierAlt = 2;
    constexpr std::int32_t ModifierShift = 4;
    constexpr std::int32_t ModifierWindows = 8;

    struct KeyCommand
    {
        std::uint32_t magic;
        std::int32_t virtualKey;
        std::int32_t modifiers;
    };

    std::wstring PackageFamilyNameForProcess(HANDLE process)
    {
        UINT32 familyNameLength = 0;
        if (GetPackageFamilyName(process, &familyNameLength, nullptr)
            != ERROR_INSUFFICIENT_BUFFER)
        {
            return {};
        }

        std::vector<wchar_t> familyName(familyNameLength);
        if (GetPackageFamilyName(process, &familyNameLength, familyName.data())
            != ERROR_SUCCESS)
        {
            return {};
        }

        return familyName.data();
    }

    std::wstring AppContainerSidForCurrentPackage()
    {
        const auto currentFamily = PackageFamilyNameForProcess(GetCurrentProcess());
        if (currentFamily.empty())
        {
            return {};
        }

        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE)
        {
            return {};
        }

        std::wstring result;
        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        for (BOOL hasEntry = Process32FirstW(snapshot, &entry);
            hasEntry && result.empty();
            hasEntry = Process32NextW(snapshot, &entry))
        {
            if (entry.th32ProcessID == GetCurrentProcessId())
            {
                continue;
            }

            HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE,
                entry.th32ProcessID);
            if (process == nullptr)
            {
                continue;
            }

            if (PackageFamilyNameForProcess(process) == currentFamily)
            {
                HANDLE token = nullptr;
                if (OpenProcessToken(process, TOKEN_QUERY, &token))
                {
                    DWORD tokenLength = 0;
                    GetTokenInformation(token, TokenAppContainerSid, nullptr, 0, &tokenLength);
                    std::vector<std::uint8_t> tokenData(tokenLength);
                    if (tokenLength != 0
                        && GetTokenInformation(token, TokenAppContainerSid,
                            tokenData.data(), tokenLength, &tokenLength))
                    {
                        const auto info = reinterpret_cast<PTOKEN_APPCONTAINER_INFORMATION>(
                            tokenData.data());
                        LPWSTR sidText = nullptr;
                        if (info->TokenAppContainer != nullptr
                            && ConvertSidToStringSidW(info->TokenAppContainer, &sidText)
                            && sidText != nullptr)
                        {
                            result = sidText;
                            LocalFree(sidText);
                        }
                    }
                    CloseHandle(token);
                }
            }
            CloseHandle(process);
        }

        CloseHandle(snapshot);
        return result;
    }

    std::wstring PipeNameForAppContainer(const std::wstring& packageSid)
    {
        DWORD sessionId = 0;
        if (packageSid.empty()
            || !ProcessIdToSessionId(GetCurrentProcessId(), &sessionId))
        {
            return LR"(\\.\pipe\LOCAL\KeyOverlay.Input)";
        }

        return LR"(\\.\pipe\Sessions\)" + std::to_wstring(sessionId)
            + LR"(\AppContainerNamedObjects\)" + packageSid
            + LR"(\KeyOverlay.Input)";
    }

    bool IsExtendedKey(WORD virtualKey)
    {
        switch (virtualKey)
        {
        case VK_RMENU:
        case VK_RCONTROL:
        case VK_INSERT:
        case VK_DELETE:
        case VK_HOME:
        case VK_END:
        case VK_PRIOR:
        case VK_NEXT:
        case VK_LEFT:
        case VK_UP:
        case VK_RIGHT:
        case VK_DOWN:
        case VK_NUMLOCK:
        case VK_CANCEL:
        case VK_SNAPSHOT:
        case VK_DIVIDE:
            return true;
        default:
            return false;
        }
    }

    INPUT KeyboardInput(WORD virtualKey, bool keyUp)
    {
        INPUT input{};
        input.type = INPUT_KEYBOARD;
        const UINT scanCode = MapVirtualKeyW(virtualKey, MAPVK_VK_TO_VSC);
        if (scanCode != 0)
        {
            input.ki.wScan = static_cast<WORD>(scanCode & 0xFF);
            input.ki.dwFlags = KEYEVENTF_SCANCODE;
        }
        else
        {
            input.ki.wVk = virtualKey;
        }
        if (keyUp)
        {
            input.ki.dwFlags |= KEYEVENTF_KEYUP;
        }
        if (IsExtendedKey(virtualKey))
        {
            input.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
        }
        return input;
    }

    void AddModifier(std::vector<INPUT>& inputs, std::int32_t modifiers, std::int32_t flag, WORD key)
    {
        if ((modifiers & flag) != 0)
        {
            inputs.push_back(KeyboardInput(key, false));
        }
    }

    void ReleaseModifier(std::vector<INPUT>& inputs, std::int32_t modifiers, std::int32_t flag, WORD key)
    {
        if ((modifiers & flag) != 0)
        {
            inputs.push_back(KeyboardInput(key, true));
        }
    }

    std::uint32_t SendKey(const KeyCommand& command)
    {
        if (command.virtualKey <= 0 || command.virtualKey > 0xFF)
        {
            return 0x80010000u;
        }

        std::vector<INPUT> downInputs;
        downInputs.reserve(5);
        AddModifier(downInputs, command.modifiers, ModifierControl, VK_CONTROL);
        AddModifier(downInputs, command.modifiers, ModifierShift, VK_SHIFT);
        AddModifier(downInputs, command.modifiers, ModifierAlt, VK_MENU);
        AddModifier(downInputs, command.modifiers, ModifierWindows, VK_LWIN);

        const auto virtualKey = static_cast<WORD>(command.virtualKey);
        downInputs.push_back(KeyboardInput(virtualKey, false));
        SetLastError(ERROR_SUCCESS);
        const UINT downSent = SendInput(
            static_cast<UINT>(downInputs.size()), downInputs.data(), sizeof(INPUT));
        const DWORD downError = GetLastError();

        std::this_thread::sleep_for(std::chrono::milliseconds(36));

        std::vector<INPUT> upInputs;
        upInputs.reserve(5);
        upInputs.push_back(KeyboardInput(virtualKey, true));
        ReleaseModifier(upInputs, command.modifiers, ModifierWindows, VK_LWIN);
        ReleaseModifier(upInputs, command.modifiers, ModifierAlt, VK_MENU);
        ReleaseModifier(upInputs, command.modifiers, ModifierShift, VK_SHIFT);
        ReleaseModifier(upInputs, command.modifiers, ModifierControl, VK_CONTROL);
        SetLastError(ERROR_SUCCESS);
        const UINT upSent = SendInput(
            static_cast<UINT>(upInputs.size()), upInputs.data(), sizeof(INPUT));
        const DWORD upError = GetLastError();

        if (downSent == static_cast<UINT>(downInputs.size())
            && upSent == static_cast<UINT>(upInputs.size()))
        {
            return 1u;
        }

        const DWORD error = downError != ERROR_SUCCESS ? downError : upError;
        return 0x80000000u
            | ((downSent & 0x7Fu) << 24)
            | ((upSent & 0xFFu) << 16)
            | (error & 0xFFFFu);
    }

    void ServeClient(HANDLE pipe)
    {
        KeyCommand command{};
        DWORD bytesRead = 0;
        const BOOL read = ReadFile(pipe, &command, sizeof(command), &bytesRead, nullptr);
        const std::uint32_t result = read != FALSE
            && bytesRead == sizeof(command)
            && command.magic == CommandMagic
            ? SendKey(command)
            : 0x80020000u;

        DWORD bytesWritten = 0;
        WriteFile(pipe, &result, sizeof(result), &bytesWritten, nullptr);
        FlushFileBuffers(pipe);
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    HANDLE instanceMutex = CreateMutexW(nullptr, TRUE, L"Local\\KeyOverlay.InputBridge.Singleton");
    if (instanceMutex == nullptr || GetLastError() == ERROR_ALREADY_EXISTS)
    {
        if (instanceMutex != nullptr)
        {
            CloseHandle(instanceMutex);
        }
        return 0;
    }

    PSECURITY_DESCRIPTOR descriptor = nullptr;
    SECURITY_ATTRIBUTES security{};
    security.nLength = sizeof(security);
    security.bInheritHandle = FALSE;
    const auto packageSid = AppContainerSidForCurrentPackage();
    const auto pipeName = PipeNameForAppContainer(packageSid);
    const auto pipeSddl = packageSid.empty()
        ? std::wstring(L"D:(A;;GRGW;;;AC)(A;;GRGW;;;WD)")
        : std::wstring(L"D:(A;;GA;;;") + packageSid
            + L")(A;;GA;;;WD)S:(ML;;NW;;;LW)";
    if (ConvertStringSecurityDescriptorToSecurityDescriptorW(
        pipeSddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr))
    {
        security.lpSecurityDescriptor = descriptor;
    }

    while (true)
    {
        HANDLE pipe = CreateNamedPipeW(
            pipeName.c_str(),
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
            1,
            128,
            128,
            0,
            descriptor == nullptr ? nullptr : &security);

        if (pipe == INVALID_HANDLE_VALUE)
        {
            break;
        }

        const BOOL connected = ConnectNamedPipe(pipe, nullptr)
            ? TRUE
            : GetLastError() == ERROR_PIPE_CONNECTED;
        if (connected)
        {
            ServeClient(pipe);
        }

        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }

    if (descriptor != nullptr)
    {
        LocalFree(descriptor);
    }
    ReleaseMutex(instanceMutex);
    CloseHandle(instanceMutex);
    return 0;
}
