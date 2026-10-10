# GPC

Windows app that launches the `trs80gp` TRS-80 emulator and wraps it with a bezel overlay (Model I / III / 4 looks),
keeping the two windows locked together (move or resize one and the other follows).
Optional GPU rendering adds bloom, brightness above 100%, curvature, scanlines, vignette, tint and a glass cover.
A WebView2 settings window exposes all look settings; they are saved to (and loaded from) `.gpc` preset files.

![Sample](docs/Sample.png)

## Download and run
1. Download `GPC-x.y.zip` from the [Releases](../../releases/latest) page and extract it anywhere (keep the folders together).
2. Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) if you don't have it.
3. Download `trs80gp.exe` from https://48k.ca/trs80gp.html and put it in the `bin` folder (or set its path in the settings window).
4. Run `bin\GPC.exe presets\M3.48K.Cheap.gpc` (Windows 10/11 x64).

**Where to extract:** GPC saves your settings back into the `.gpc` preset file (and writes its logs next to it), so the `presets` folder must be writable.
Extract to your user folder (e.g. `%USERPROFILE%\GPC`) or another writable folder such as `C:\GPC`.
If you do extract to `C:\Program Files`, standard users can't write there; grant write access to the presets folder only (elevated prompt):

    icacls "C:\Program Files\GPC\presets" /grant "Users:(OI)(CI)M"

To build it yourself instead, see [BUILD.md](BUILD.md) (**Code > Download ZIP** gives the source).

Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) and the
[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (already present on Windows 11).

## Default hotkeys
| Keys | Action |
|---|---|
| Ctrl+Alt+F | Strip / restore the emulator frame and menu |
| Ctrl+Alt+O | Toggle overlay |
| Ctrl+Alt+S | Show settings window |

All hotkeys are editable in the settings window.

<!-- CLAUDE : new section -->
## MCP interface
GPC runs a local MCP server on the named pipe `\\.\pipe\GPC.<pid>` (JSON-RPC 2.0, one request per line, current user only). It is on by default and can be switched off with `[Pipe] Enabled` in the preset. Every tool runs on GPC's UI thread, so it behaves like the settings window.

There is one pipe per running instance, so several GPCs can run side by side. `tools/GPCPipeClient.cs` is a small C# client for talking to a pipe directly (instances are found by process name, pipe = `GPC.<pid>`).

| Tool | Purpose |
|---|---|
| `gpc_get_status` | Emulator and overlay state, current preset and profile, unsaved-changes flag. |
| `gpc_list_settings` | Every setting (`Section.Key`) and its current value; optional `section` filter. |
| `gpc_get_setting` | Current value of one setting (`id`). |
| `gpc_set_setting` | Set one setting (`id`, `value` as text), live-applied like the settings window. |
| `gpc_preset_load` | Load a `.gpc` file and relaunch the emulator; refused with unsaved changes unless `discard=true`. |
| `gpc_preset_save` / `gpc_preset_save_as` | Save the current preset / save to a new `.gpc` file and make it current. |
| `gpc_toggle_frame` | Strip / restore the emulator frame and menu. |
| `gpc_toggle_overlay` | Toggle the overlay. |
| `gpc_relaunch` | Kill and restart the emulator. |
| `gpc_power_click` | Click the power rect (`double=true` also pauses / cold restarts the emulator). |
| `gpc_power_set` | Switch the monitor on or off (`on`, optional `full`). |
| `gpc_reset_power` | Reset the power rectangle. |
| `gpc_show_settings` | Show the settings window. |
| `gpc_send_keys`, `gpc_keys_status`, `gpc_keys_cancel` | Type a script into the emulator (below). |
| `gpc_exit` | Quit GPC; refused with unsaved changes unless `discard=true`. |

Resources: `gpc://status` and `gpc://settings` (the same data as `gpc_get_status` and `gpc_list_settings`).

There is no authentication: anything that can open the pipe (your own user account only) can quit GPC or change its settings.

<!-- CLAUDE : end new section -->
## Keyboard input scripts (MCP)
Three tools type a script into the emulator, for example to get a program through its initial setup screens:

| Tool | Purpose |
|---|---|
| `gpc_send_keys` | Start a script: `path` (a file GPC reads) or `text`, optional `charDelayMs` and `lineDelayMs`. Returns at once; the script runs in the background. |
| `gpc_keys_status` | `idle` / `running` / `done` / `cancelled` / `error`, the current line, and any error text. |
| `gpc_keys_cancel` | Stop the running script (any held key is released). |

Only one script runs at a time. The whole script is checked first: an error such as an unsupported character is reported with its line and column, and nothing is typed. Relaunching the emulator or quitting GPC cancels a running script.

**Script format**
- Each line is typed, then Enter is pressed. Typeable text is `A-Z`, `a-z` (typed as the unshifted key), `0-9` and space. An empty line is just Enter.
- A line starting with `;` is a comment.
- `{NAME}` or `{NAME n}` presses a key (n times): `ENTER` `BREAK` (Esc) `UP` `DOWN` `LEFT` `RIGHT` `SPACE`. `{KEY hex}` or `{KEY hex n}` presses any Windows virtual key, e.g. `{KEY 1B}`.
- `{DELAY ms}` waits. `{CHARDELAY ms}` and `{LINEDELAY ms}` change the gap after each key / after each line's Enter for the rest of the script.
- `{KEYDOWN key}` holds a key down until `{KEYUP key}` (key = a name such as `SPACE`, or a hex virtual key). A key still held when the script ends or is cancelled is released. Add `{NOEOL}` to the line, or Enter is pressed after it. Example: `{KEYDOWN 7B}{NOEOL}` `{DELAY 5000}{NOEOL}` `{KEYUP 7B}{NOEOL}`.
- `{NOEOL}` anywhere on a line = no Enter at the end of that line.
- Flow: `{LABEL name}`, `{GOTO name}`, `{LOOP name n}` (jump back to `name` n times, then continue) and `{REPEAT n}` ... `{END}` (nestable). An endless `{GOTO}` loop is stopped with `gpc_keys_cancel`.

Shifted characters (`! " ( ? @` ...) cannot be typed: the emulator reads Shift from the real keyboard, which posted keys cannot change. Use `{KEY hex}` for other unshifted keys.

Defaults are saved in the preset's `[Keys]` section: `CharDelayMs=40`, `LineDelayMs=300`, `HoldMs=60` (how long each key stays down).

Example: boot a Model 4 into BASIC, enter a loop, run it, then press BREAK.
```
; the two empty lines answer the Cass? and Memory Size? prompts


{DELAY 1500}{NOEOL}
10 GOTO 10
RUN
{DELAY 1500}{BREAK}{NOEOL}
```
Leave time (`{DELAY}`) after the Enter that starts BASIC: keys sent while it initializes are lost.

## Layout
| Folder | Contents |
|---|---|
| `src` | C# source and `APP.csproj` |
| `lib` | `nini-core.dll` (settings library, referenced by the build) |
| `bin` | `GPC.exe` (release zip, or after building; add `trs80gp.exe` here) |
| `assets` | Bezel images and icon |
| `presets` | Sample `.gpc` look presets (load from the settings window) |
| `programs` | Sample cassette images, and the BASIC / key-script samples used by `scripts\RandomLaunch.ps1` |
| `scripts` | PowerShell / batch helpers that drive a running GPC over its pipe (CRT power, send keys, random launcher) |
| `tools` | `GPCPipeClient.cs`, a C# pipe client to copy into your own program |
| `Package.ps1` | Builds the release zip |

## Notes
- `trs80gp.exe` is a separate third-party emulator by its own author and is NOT included. Get it from https://48k.ca/trs80gp.html
- Released under the [MIT License](LICENSE) © Joe Costolnick ([@fastdogjo](https://x.com/fastdogjo)). No support, no contributions. The bezel images in `assets` are original work covered by the same license.
- Uses third-party libraries under their own permissive licenses: Microsoft WebView2, Silk.NET, and Nini.
- Sample command lines :
   "C:/Program Files/GPC/bin/gpc.exe" "C:/Program Files/GPC/presets/M4.64K.Authentic.Glass.Dr.Fill.gpc"
   "C:/GPC/bin/gpc.exe" "C:/GPC/presets/M4.64K.Authentic.Small.gpc" "C:/GPC/programs/Dr.Fill.cas"
