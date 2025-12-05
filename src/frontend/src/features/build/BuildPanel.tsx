import React, { useEffect, useState } from "react";
import { baseUrl } from "../../shared/config";

type Diag = { severity: string; Message: string; Line: number };

export default function BuildPanel({ currentPou, onSelectPou, onSwitchToSt }: { currentPou?: string; onSelectPou?: (name: string) => void; onSwitchToSt?: () => void }) {
  const base = baseUrl;
  const [text, setText] = useState<string>("PROGRAM Main\nVAR\n    x : INT;\nEND_VAR\nx := x + 1;\nEND_PROGRAM");
  const [diags, setDiags] = useState<Diag[]>([]);
  const [pouDiags, setPouDiags] = useState<Diag[]>([]);
  const [pouText, setPouText] = useState<string>("");
  const [projectResults, setProjectResults] = useState<{ name: string; diagnostics: Diag[] }[]>([]);
  const [filter, setFilter] = useState<"All" | "Error" | "Warning" | "Info">("All");
  const flattened = projectResults.flatMap(it => it.diagnostics.map(d => ({ ...d, pou: it.name })));

  const compileText = async () => {
    const res = await fetch(`${base}/compile/st`, { method: "POST", headers: { "Content-Type": "text/plain" }, body: text }).catch(() => null);
    if (!res || !res.ok) return;
    const data = await res.json(); setDiags(data.diagnostics ?? []);
  };

  const compileCurrentPou = async () => {
    if (!currentPou) return;
    const resPou = await fetch(`${base}/compile/pou?name=${encodeURIComponent(currentPou)}`).catch(() => null);
    if (!resPou || !resPou.ok) return;
    const data = await resPou.json();
    const t = data.text ?? ""; setPouText(t);
    const res = await fetch(`${base}/compile/st`, { method: "POST", headers: { "Content-Type": "text/plain" }, body: t }).catch(() => null);
    if (!res || !res.ok) return;
    const data2 = await res.json(); setPouDiags(data2.diagnostics ?? []);
  };

  const compileProject = async () => {
    const res = await fetch(`${base}/compile/project`).catch(() => null);
    if (!res || !res.ok) return;
    const data = await res.json();
    const items = (data.items ?? []) as any[];
    setProjectResults(items.map(it => ({ name: it.name, diagnostics: it.diagnostics ?? [] })));
  };

  return (
    <div style={{ padding: 12, display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
      <div>
        <div style={{ fontWeight: 600 }}>编译编辑器内容</div>
        <textarea rows={12} style={{ width: "100%" }} value={text} onChange={e => setText(e.target.value)} />
        <div style={{ marginTop: 8, display: "flex", gap: 8 }}>
          <button onClick={compileText}>编译</button>
          <button onClick={async () => {
            const blob = new Blob([text], { type: "text/plain" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a"); a.href = url; a.download = "editor.st"; a.click(); URL.revokeObjectURL(url);
          }}>导出ST</button>
          <select value={filter} onChange={e => setFilter(e.target.value as any)}>
            <option>All</option>
            <option>Error</option>
            <option>Warning</option>
            <option>Info</option>
          </select>
        </div>
        <div style={{ marginTop: 8 }}>
          {diags.filter(d => filter === "All" || d.severity === filter).map((d, i) => (<div key={i} style={{ fontSize: 12 }}>{d.severity} L{d.Line}: {d.Message}</div>))}
        </div>
      </div>
      <div>
        <div style={{ fontWeight: 600 }}>编译当前POU生成的ST {currentPou ? `(${currentPou})` : ""}</div>
        <div style={{ display: "flex", gap: 8, marginBottom: 8 }}>
          <button onClick={compileCurrentPou} disabled={!currentPou}>编译当前POU</button>
          <button onClick={async () => {
            if (!currentPou) return;
            const res = await fetch(`${base}/export/pou?name=${encodeURIComponent(currentPou)}`);
            const xml = await res.text(); const blob = new Blob([xml], { type: "application/xml" });
            const url = URL.createObjectURL(blob); const a = document.createElement("a"); a.href = url; a.download = `${currentPou}.xml`; a.click(); URL.revokeObjectURL(url);
          }} disabled={!currentPou}>导出POU XML</button>
          <button onClick={async () => {
            const res = await fetch(`${base}/export/project`);
            const xml = await res.text(); const blob = new Blob([xml], { type: "application/xml" });
            const url = URL.createObjectURL(blob); const a = document.createElement("a"); a.href = url; a.download = "project.xml"; a.click(); URL.revokeObjectURL(url);
          }}>导出工程XML</button>
          <button onClick={compileProject}>编译工程</button>
        </div>
        <textarea rows={12} style={{ width: "100%" }} value={pouText} readOnly />
        <div style={{ marginTop: 8 }}>
          {pouDiags.map((d, i) => (<div key={i} style={{ fontSize: 12 }}>{d.severity} L{d.Line}: {d.Message}</div>))}
        </div>
        {projectResults.length > 0 && (
          <div style={{ marginTop: 12 }}>
            <div style={{ fontWeight: 600 }}>工程诊断</div>
            {projectResults.map((it, idx) => (
              <div key={idx} style={{ marginTop: 4 }}>
                <button onClick={() => { onSelectPou?.(it.name); onSwitchToSt?.(); }} style={{ fontWeight: 600 }}>{it.name}</button>
                {it.diagnostics.map((d, i) => (
                  <button key={i} style={{ fontSize: 12, paddingLeft: 8, textAlign: "left" }} onClick={() => {
                    try { localStorage.setItem("st_jump", JSON.stringify({ pou: it.name, line: (d as any).Line, column: (d as any).column ?? 1 })); } catch {}
                    onSelectPou?.(it.name); onSwitchToSt?.();
                  }}>
                    {d.severity} {d.code ? `[${(d as any).code}] ` : ""}{d.Message}
                  </button>
                ))}
              </div>
            ))}
            <div style={{ marginTop: 8 }}>
              <div style={{ fontWeight: 600 }}>问题列表（聚合）</div>
              {flattened.filter(d => filter === "All" || d.severity === filter).map((d, i) => (
                <button key={i} style={{ fontSize: 12, textAlign: "left" }} onClick={() => {
                  try { localStorage.setItem("st_jump", JSON.stringify({ pou: (d as any).pou, line: (d as any).Line, column: (d as any).column ?? 1 })); } catch {}
                  onSelectPou?.((d as any).pou); onSwitchToSt?.();
                }}>
                  {(d as any).pou}: {d.severity} {d.Message}
                </button>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
