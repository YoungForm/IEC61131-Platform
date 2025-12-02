import React, { useEffect, useState } from "react";

type POU = { Name: string; type: string };
type VarItem = { name: string; type: string; address?: string; domain: string };

export default function PouManager({ currentPou, onSelect }: { currentPou?: string; onSelect: (name: string) => void }) {
  const [list, setList] = useState<POU[]>([]);
  const [name, setName] = useState("");
  const [type, setType] = useState("Program");
  const [vars, setVars] = useState<VarItem[]>([]);
  const [vname, setVname] = useState("");
  const [vtype, setVtype] = useState("BOOL");
  const [vaddr, setVaddr] = useState("");
  const [vdomain, setVdomain] = useState("local");
  const [addrValid, setAddrValid] = useState<boolean | null>(null);
  const baseUrl = "http://localhost:5000";
  const load = () => fetch(`${baseUrl}/pous/list`).then(r => r.json()).then(setList).catch(() => {});
  useEffect(() => { load(); }, []);
  useEffect(() => {
    if (!currentPou) { setVars([]); return; }
    fetch(`${baseUrl}/pous/vars?name=${encodeURIComponent(currentPou)}`).then(r => r.json()).then(setVars).catch(() => {});
  }, [currentPou]);
  const create = async () => {
    const qs = new URLSearchParams({ name, type }).toString();
    await fetch(`${baseUrl}/pous/create?${qs}`, { method: "POST" });
    setName("");
    load();
  };
  const del = async (n: string) => { await fetch(`${baseUrl}/pous/delete?name=${encodeURIComponent(n)}`, { method: "POST" }); load(); };
  return (
    <div>
      <div style={{ fontWeight: 600, marginBottom: 8 }}>POU</div>
      <div style={{ marginBottom: 8 }}>
        <input placeholder="名称" value={name} onChange={e => setName(e.target.value)} />
        <select value={type} onChange={e => setType(e.target.value)}>
          <option>Program</option>
          <option>FunctionBlock</option>
          <option>Function</option>
        </select>
        <button onClick={create}>创建</button>
      </div>
      {list.map(p => (
        <div key={p.Name} style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <button onClick={() => onSelect(p.Name)}>{p.Name}</button>
          <span>{p.type}</span>
          <button onClick={() => del(p.Name)}>删除</button>
        </div>
      ))}
      {currentPou && (
        <div style={{ marginTop: 12, borderTop: "1px solid #ddd", paddingTop: 8 }}>
          <div style={{ fontWeight: 600, marginBottom: 6 }}>变量（{currentPou}）</div>
          <div style={{ display: "grid", gridTemplateColumns: "1fr", gap: 6 }}>
            <input placeholder="名称" value={vname} onChange={e => setVname(e.target.value)} />
            <select value={vtype} onChange={e => setVtype(e.target.value)}>
              <option>BOOL</option>
              <option>INT</option>
              <option>REAL</option>
              <option>STRING</option>
              <option>TIME</option>
            </select>
            <input placeholder="地址" value={vaddr} onChange={async e => {
              const v = e.target.value; setVaddr(v);
              if (v) { const ok = await fetch(`${baseUrl}/vars/validate?address=${encodeURIComponent(v)}`).then(r => r.json()).catch(() => ({ ok: false })); setAddrValid(!!ok.ok); } else { setAddrValid(null); }
            }} />
            <div style={{ fontSize: 12, color: addrValid == null ? "#666" : addrValid ? "green" : "red" }}>{addrValid == null ? "地址可选" : addrValid ? "地址有效" : "地址无效"}</div>
            <select value={vdomain} onChange={e => setVdomain(e.target.value)}>
              <option value="input">输入</option>
              <option value="output">输出</option>
              <option value="local">局部</option>
            </select>
            <button onClick={async () => {
              if (!currentPou || !vname) return;
              const qs = new URLSearchParams({ name: currentPou, vname, type: vtype, address: vaddr, domain: vdomain }).toString();
              await fetch(`${baseUrl}/pous/vars/upsert?${qs}`, { method: "POST" });
              const list = await fetch(`${baseUrl}/pous/vars?name=${encodeURIComponent(currentPou)}`).then(r => r.json()).catch(() => []);
              setVars(list); setVname(""); setVaddr(""); setAddrValid(null);
            }}>添加/更新变量</button>
          </div>
          <div style={{ marginTop: 8 }}>
            {vars.map(v => (
              <div key={`${v.domain}-${v.name}`} style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <span style={{ minWidth: 48, fontSize: 12, color: "#666" }}>{v.domain}</span>
                <span style={{ flex: 1 }}>{v.name}:{v.type}{v.address ? `@${v.address}` : ""}</span>
                <button onClick={async () => {
                  if (!currentPou) return;
                  const qs = new URLSearchParams({ name: currentPou, vname: v.name }).toString();
                  await fetch(`${baseUrl}/pous/vars/delete?${qs}`, { method: "POST" });
                  const list = await fetch(`${baseUrl}/pous/vars?name=${encodeURIComponent(currentPou)}`).then(r => r.json()).catch(() => []);
                  setVars(list);
                }}>删除</button>
              </div>
            ))}
          </div>
        </div>
      )}
      <div style={{ marginTop: 12 }}>
        <button onClick={async () => {
          const res = await fetch(`${baseUrl}/export/project`);
          const xml = await res.text();
          const blob = new Blob([xml], { type: "application/xml" });
          const url = URL.createObjectURL(blob);
          const a = document.createElement("a");
          a.href = url; a.download = "project.xml"; a.click(); URL.revokeObjectURL(url);
        }}>导出工程XML</button>
        <button onClick={async () => { await fetch(`${baseUrl}/project/save`, { method: "POST" }); }}>保存工程</button>
        <button onClick={async () => { await fetch(`${baseUrl}/project/load`, { method: "POST" }); load(); }}>加载工程</button>
        <button onClick={async () => {
          const res = await fetch(`${baseUrl}/project/export-json`);
          const json = await res.json();
          const blob = new Blob([JSON.stringify(json, null, 2)], { type: "application/json" });
          const url = URL.createObjectURL(blob);
          const a = document.createElement("a");
          a.href = url; a.download = "workspace.json"; a.click(); URL.revokeObjectURL(url);
        }}>导出工程JSON</button>
        <label style={{ border: "1px solid #ccc", padding: "2px 6px", cursor: "pointer", marginLeft: 8 }}>
          导入工程JSON
          <input type="file" accept=".json" style={{ display: "none" }} onChange={async (e) => {
            const f = e.target.files?.[0]; if (!f) return;
            const txt = await f.text();
            await fetch(`${baseUrl}/project/import-json`, { method: "POST", headers: { "Content-Type": "application/json" }, body: txt });
            load(); e.currentTarget.value = "";
          }} />
        </label>
      </div>
    </div>
  );
}
