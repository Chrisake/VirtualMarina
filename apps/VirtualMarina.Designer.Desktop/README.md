# VirtualMarina Designer — desktop launcher

Runs the Blazor designer (`apps/VirtualMarina.Designer.Blazor`) as a windowed app on Windows, macOS and Linux. It
serves the WebAssembly build on a free loopback port and opens it in a Chromium-family browser in application mode
(a window with no tabs or address bar). It is not a native wrapper: the page is the ordinary app in the ordinary
browser engine.

## Publish it before handing it out

```
dotnet publish apps/VirtualMarina.Designer.Desktop -c Release -o out/designer
out/designer/VirtualMarina.Designer.Desktop        # VirtualMarina.Designer.Desktop.exe on Windows
```

Publish, don't copy the build output. A plain build serves the client's files through ASP.NET Core's *static web
assets* manifest, which points at the source and `obj` folders of the machine it was built on; it runs there
(`dotnet run --project apps/VirtualMarina.Designer.Desktop`) and nowhere else. `dotnet publish` copies everything
into the output's `wwwroot`, which the launcher serves from its own folder whatever the working directory.

## Which window opens

Tried in this order, the first one found wins:

1. `VIRTUALMARINA_BROWSER`, if it names an executable.
2. Google Chrome, Microsoft Edge, Chromium, Brave — `Program Files` / `%LOCALAPPDATA%` on Windows, `/Applications`
   and `~/Applications` on macOS, the `PATH` and `/snap/bin` on Linux.
3. On Linux, the same browsers installed as a Flatpak.
4. The default browser, as an ordinary tab.

`--no-browser` opens nothing and only prints the address.

A browser the launcher can start as a process of its own gets a profile of its own, kept between launches under the
user's application data (`%LOCALAPPDATA%\VirtualMarina\Designer\BrowserProfiles` on Windows,
`~/.local/share/VirtualMarina/Designer/BrowserProfiles` on Linux, `~/Library/Application Support/...` on macOS),
readable by that user alone. Snaps and Flatpaks cannot use it, so they open the window in the user's usual profile.

## When it stops

The launcher stops when its window closes, however the window was opened. The page (only when the launcher served it:
it is opened with `?launcher=<token>`) keeps a Server-Sent Events stream open to `/launcher/events` and says goodbye on
`/launcher/bye` when it is closed. About eight seconds after the last page has gone — long enough for a reload — the
launcher shuts down. If no page connects within a minute of starting, it gives up.

A Flatpak or snap browser, a browser that was already running, and the default browser all put the window in a
process the launcher cannot wait on, which is why the page itself reports in. Where the launcher did start the window's
own process, that process ending stops it too.

Ctrl+C in the console stops the launcher and closes the window it opened.

The endpoints under `/launcher/` need the token from the page's address; without it they answer 404, as any static
host would, and the designer served by any static host never asks for them.
