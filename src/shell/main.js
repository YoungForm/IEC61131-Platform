const { app, BrowserWindow } = require("electron");
const path = require("path");
const { spawn } = require("child_process");

let backend;

function createWindow() {
  const win = new BrowserWindow({
    width: 1280,
    height: 800,
    webPreferences: { nodeIntegration: false, contextIsolation: true }
  });
  const devUrl = "http://localhost:5173";
  win.loadURL(devUrl);
}

app.whenReady().then(() => {
  backend = spawn("dotnet", ["run", "--project", path.join(__dirname, "../backend/Plc.Server")], { stdio: "ignore" });
  createWindow();
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});

app.on("quit", () => {
  if (backend) backend.kill();
});

