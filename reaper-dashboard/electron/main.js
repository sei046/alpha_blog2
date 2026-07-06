// Electron main process for REAPER Project Dashboard.
//
// Responsibilities are deliberately small: create the window, own native
// file dialogs, and expose a handful of app paths over IPC. All domain
// logic lives in the Fable (F#) renderer code under src/.
const { app, BrowserWindow, dialog, ipcMain, shell } = require("electron");
const path = require("path");

let mainWindow = null;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1480,
    height: 940,
    minWidth: 1100,
    minHeight: 680,
    backgroundColor: "#16181d",
    // macOS: content flows under a hidden title bar for a native studio look.
    titleBarStyle: process.platform === "darwin" ? "hiddenInset" : "default",
    webPreferences: {
      // MVP: the renderer is trusted local F# code that needs fs access to
      // scan .rpp files. It never loads remote content (see will-navigate
      // guard below). Tightening to a preload/contextBridge API is on the
      // roadmap before any feature that renders untrusted strings as HTML.
      nodeIntegration: true,
      contextIsolation: false,
    },
  });

  mainWindow.loadFile(path.join(__dirname, "..", "public", "index.html"));

  // Never navigate away from the local app, and open any external link
  // in the system browser instead of inside the app window.
  mainWindow.webContents.on("will-navigate", (event) => event.preventDefault());
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith("https://")) shell.openExternal(url);
    return { action: "deny" };
  });

  mainWindow.on("closed", () => {
    mainWindow = null;
  });
}

// --- IPC: native dialogs and app paths -------------------------------------

ipcMain.handle("dialog:choose-rpp-files", async (_event, defaultPath) => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: "Import REAPER projects",
    defaultPath: defaultPath || undefined,
    properties: ["openFile", "multiSelections"],
    filters: [{ name: "REAPER Project", extensions: ["rpp", "RPP"] }],
  });
  return result.canceled ? [] : result.filePaths;
});

ipcMain.handle("dialog:choose-folder", async (_event, opts) => {
  const { title, defaultPath } = opts || {};
  const result = await dialog.showOpenDialog(mainWindow, {
    title: title || "Choose folder",
    defaultPath: defaultPath || undefined,
    properties: ["openDirectory"],
  });
  return result.canceled ? "" : result.filePaths[0];
});

ipcMain.handle("dialog:choose-app", async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: "Locate REAPER",
    defaultPath: "/Applications",
    properties: ["openFile"],
    // .app bundles are selectable as packages in the macOS open-file dialog.
    filters:
      process.platform === "darwin"
        ? [{ name: "Application", extensions: ["app"] }]
        : [],
  });
  return result.canceled ? "" : result.filePaths[0];
});

ipcMain.handle("app:get-paths", () => ({
  userData: app.getPath("userData"),
  home: app.getPath("home"),
}));

// --- App lifecycle ----------------------------------------------------------

app.whenReady().then(() => {
  createWindow();
  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});
