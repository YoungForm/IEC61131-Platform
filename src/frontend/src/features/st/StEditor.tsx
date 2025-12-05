import React, { useEffect, useRef, useState } from "react";
import * as monaco from "monaco-editor";
import { registerStLanguage } from "./monaco";
import { baseUrl } from "../../shared/config";

export default function StEditor({ currentPou }: { currentPou?: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const [compileInfo, setCompileInfo] = useState<{ program?: string; count: number } | null>(null);
  const [compileVars, setCompileVars] = useState<{ name: string; type: string; domain?: string; line?: number }[]>([]);
  const hoverDisposeRef = useRef<monaco.IDisposable | null>(null);
  const [refsCount, setRefsCount] = useState<number>(0);
  const [refs, setRefs] = useState<{ line: number; startColumn: number; endColumn: number }[]>([]);
  const [refIndex, setRefIndex] = useState<number>(0);
  const [compileDiags, setCompileDiags] = useState<{ severity: string; Message: string; Line: number }[]>([]);
  const decorationsRef = useRef<string[]>([]);
  const base = baseUrl;
  useEffect(() => {
    registerStLanguage();
    const editor = monaco.editor.create(ref.current!, {
      value: "PROGRAM Main\nVAR\n    x : INT;\nEND_VAR\nIF x > 0 THEN\n    x := x + 1;\nEND_IF\nEND_PROGRAM",
      language: "st",
      automaticLayout: true,
      theme: "vs"
    });
    editorRef.current = editor;
    const style = document.createElement("style");
    style.textContent = ".st-ref-highlight{ background: rgba(255,230,150,0.6); }";
    document.head.appendChild(style);
    return () => { editor.dispose(); editorRef.current = null; };
  }, []);
  useEffect(() => {
    if (!currentPou || !editorRef.current) return;
    (async () => {
      const res = await fetch(`${base}/pous/st?name=${encodeURIComponent(currentPou)}`).catch(() => null);
      if (!res || !res.ok) return;
      const text = await res.text();
      const model = editorRef.current.getModel();
      model?.setValue(text);
      try {
        const raw = localStorage.getItem("st_jump");
        if (raw) {
          const j = JSON.parse(raw);
          if (j && j.pou === currentPou) {
            editorRef.current.revealLineInCenter(j.line);
            editorRef.current.setSelection({ startLineNumber: j.line, startColumn: j.column ?? 1, endLineNumber: j.line, endColumn: (j.column ?? 1) + 1 });
            localStorage.removeItem("st_jump");
          }
        }
      } catch {}
    })();
  }, [currentPou]);
  useEffect(() => {
    if (hoverDisposeRef.current) { hoverDisposeRef.current.dispose(); hoverDisposeRef.current = null; }
    hoverDisposeRef.current = monaco.languages.registerHoverProvider("st", {
      provideHover(model, position) {
        const word = model.getWordAtPosition(position);
        if (!word) return { contents: [] } as any;
        const nm = word.word;
        const found = compileVars.find(v => v.name === nm);
        if (!found) return { contents: [] } as any;
        const dom = found.domain ? ` [${found.domain}]` : "";
        const ln = found.line ? ` (L${found.line})` : "";
        return { contents: [{ value: `${found.name} : ${found.type}${dom}${ln}` }] } as any;
      }
    });
    return () => { if (hoverDisposeRef.current) { hoverDisposeRef.current.dispose(); hoverDisposeRef.current = null; } };
  }, [compileVars]);
  return (
    <div style={{ height: "100%", display: "flex", flexDirection: "column" }}>
      <div style={{ padding: 8, borderBottom: "1px solid #ddd", display: "flex", gap: 8, alignItems: "center" }}>
        <button onClick={async () => {
          if (!editorRef.current) return;
          const model = editorRef.current.getModel();
          const text = model?.getValue() ?? "";
          const res = await fetch(`${base}/compile/st`, { method: "POST", headers: { "Content-Type": "text/plain" }, body: text }).catch(() => null);
          if (!res || !res.ok) return;
          const data = await res.json();
          const markers = (data.diagnostics ?? []).map((d: any) => ({
            severity: d.severity === "Error" ? monaco.MarkerSeverity.Error : d.severity === "Warning" ? monaco.MarkerSeverity.Warning : monaco.MarkerSeverity.Info,
            message: (d.code ? `[${d.code}] ` : "") + d.Message,
            startLineNumber: d.Line,
            startColumn: d.column ?? 1,
            endLineNumber: d.Line,
            endColumn: (d.column ?? 1) + 1
          }));
          if (model) monaco.editor.setModelMarkers(model, "st-compile", markers);
          setCompileInfo({ program: data.program, count: markers.length });
          setCompileVars((data.variables ?? []).map((v: any) => ({ name: v.name ?? v.Name, type: v.type ?? v.TypeName, domain: v.domain ?? v.Domain, line: v.line ?? v.Line })));
          setCompileDiags(data.diagnostics ?? []);
        }}>编译</button>
        <button onClick={async () => {
          if (!editorRef.current) return;
          const pos = editorRef.current.getPosition(); if (!pos) return;
          const text = editorRef.current.getModel()?.getValue() ?? "";
          const payload = JSON.stringify({ text, line: pos.lineNumber, column: pos.column });
          const res = await fetch(`${base}/lsp/st/references`, { method: "POST", headers: { "Content-Type": "application/json" }, body: payload }).catch(() => null);
          if (!res || !res.ok) return;
          const data = await res.json();
          const refs = (data.references ?? []) as any[];
          setRefsCount(refs.length ?? 0);
          setRefs(refs.map(r => ({ line: r.line, startColumn: r.startColumn, endColumn: r.endColumn })));
          setRefIndex(0);
          if (refs.length > 0) {
            const first = refs[0];
            editorRef.current.revealLineInCenter(first.line);
            editorRef.current.setSelection({ startLineNumber: first.line, startColumn: first.startColumn, endLineNumber: first.line, endColumn: first.endColumn });
          }
          const model = editorRef.current.getModel();
          if (model) {
            const newDecos = refs.map(r => ({ range: new monaco.Range(r.line, r.startColumn, r.line, r.endColumn), options: { inlineClassName: "st-ref-highlight" } }));
            decorationsRef.current = editorRef.current.deltaDecorations(decorationsRef.current, newDecos);
          }
        }}>查找引用</button>
        <button onClick={() => {
          if (!editorRef.current || refs.length === 0) return;
          const idx = (refIndex - 1 + refs.length) % refs.length; setRefIndex(idx);
          const r = refs[idx];
          editorRef.current.revealLineInCenter(r.line);
          editorRef.current.setSelection({ startLineNumber: r.line, startColumn: r.startColumn, endLineNumber: r.line, endColumn: r.endColumn });
        }}>上一引用</button>
        <button onClick={() => {
          if (!editorRef.current || refs.length === 0) return;
          const idx = (refIndex + 1) % refs.length; setRefIndex(idx);
          const r = refs[idx];
          editorRef.current.revealLineInCenter(r.line);
          editorRef.current.setSelection({ startLineNumber: r.line, startColumn: r.startColumn, endLineNumber: r.line, endColumn: r.endColumn });
        }}>下一引用</button>
        <button onClick={async () => {
          if (!editorRef.current) return;
          const pos = editorRef.current.getPosition(); if (!pos) return;
          const text = editorRef.current.getModel()?.getValue() ?? "";
          const payload = JSON.stringify({ text, line: pos.lineNumber, column: pos.column });
          const res = await fetch(`${base}/lsp/st/definition`, { method: "POST", headers: { "Content-Type": "application/json" }, body: payload }).catch(() => null);
          if (!res || !res.ok) return;
          const data = await res.json();
          if (data.found && typeof data.line === "number") {
            editorRef.current.revealLineInCenter(data.line);
            editorRef.current.setSelection({ startLineNumber: data.line, startColumn: 1, endLineNumber: data.line, endColumn: 1000 });
          }
        }}>跳转定义</button>
        <button onClick={async () => {
          if (!editorRef.current) return;
          const newName = window.prompt("输入新名称"); if (!newName) return;
          const pos = editorRef.current.getPosition(); if (!pos) return;
          const text = editorRef.current.getModel()?.getValue() ?? "";
          const payload = JSON.stringify({ text, line: pos.lineNumber, column: pos.column, newName });
          const res = await fetch(`${base}/lsp/st/rename`, { method: "POST", headers: { "Content-Type": "application/json" }, body: payload }).catch(() => null);
          if (!res || !res.ok) return;
          const data = await res.json();
          if (data.text) { editorRef.current.getModel()?.setValue(data.text); }
        }}>重命名</button>
        <button onClick={async () => {
          if (!currentPou || !editorRef.current) return;
          const res = await fetch(`${base}/pous/st?name=${encodeURIComponent(currentPou)}`).catch(() => null);
          if (!res || !res.ok) return;
          const text = await res.text();
          const model = editorRef.current.getModel();
          model?.setValue(text);
        }}>加载当前POU ST</button>
        {compileInfo && (
          <span>程序: {compileInfo.program ?? ""} 诊断: {compileInfo.count}</span>
        )}
        {refsCount > 0 && (
          <span style={{ marginLeft: 8 }}>引用: {refsCount}</span>
        )}
      </div>
      {compileDiags.length > 0 && (
        <div style={{ padding: 8, borderBottom: "1px solid #eee", display: "grid", gridTemplateColumns: "1fr", gap: 4 }}>
          {compileDiags.map((d, i) => (
            <button key={i} style={{ textAlign: "left" }} onClick={() => {
              if (!editorRef.current) return;
              editorRef.current.revealLineInCenter(d.Line);
              editorRef.current.setSelection({ startLineNumber: d.Line, startColumn: 1, endLineNumber: d.Line, endColumn: 1000 });
            }}>{d.severity} {d.Message}</button>
          ))}
        </div>
      )}
      <div style={{ flex: 1 }} ref={ref} />
    </div>
  );
}
