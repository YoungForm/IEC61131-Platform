import React, { useState } from "react";
import StEditor from "./features/st/StEditor";
import FbdEditor from "./features/fbd/FbdEditor";
import SimPanel from "./features/sim/SimPanel";
import BuildPanel from "./features/build/BuildPanel";
import PouManager from "./features/pou/PouManager";

export default function App() {
  const [view, setView] = useState<"st" | "fbd" | "sim" | "build">("st");
  const [currentPou, setCurrentPou] = useState<string>("");
  return (
    <div style={{ display: "flex", height: "100vh" }}>
      <aside style={{ width: 240, borderRight: "1px solid #ddd", padding: 12 }}>
        <h3>PLC IDE</h3>
        <button onClick={() => setView("st")}>ST 编辑器</button>
        <button onClick={() => setView("fbd")} style={{ marginLeft: 8 }}>FBD 编辑器</button>
        <button onClick={() => setView("sim")} style={{ marginLeft: 8 }}>仿真</button>
        <button onClick={() => setView("build")} style={{ marginLeft: 8 }}>构建面板</button>
        <div style={{ marginTop: 12 }}>
          <PouManager currentPou={currentPou} onSelect={setCurrentPou} />
          <div style={{ marginTop: 8 }}>当前POU：{currentPou || "未选择"}</div>
        </div>
      </aside>
      <main style={{ flex: 1 }}>
        {view === "st" ? <StEditor currentPou={currentPou} /> : view === "fbd" ? <FbdEditor currentPou={currentPou} /> : view === "build" ? <BuildPanel currentPou={currentPou} onSelectPou={setCurrentPou} onSwitchToSt={() => setView("st")} /> : <SimPanel />}
      </main>
    </div>
  );
}
