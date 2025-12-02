import React, { useCallback, useEffect, useMemo, useState } from "react";
import ReactFlow, { Background, Controls, MiniMap, Node, Edge, addEdge, Connection, useNodesState, useEdgesState } from "reactflow";
import "reactflow/dist/style.css";
import BlockNode from "./nodes/BlockNode";
import VarNode from "./nodes/VarNode";

const initialNodes: Node[] = [
  { id: "in1", position: { x: 50, y: 120 }, data: { label: "IN BOOL", portType: "BOOL", direction: "out" }, type: "input" },
  { id: "ton", position: { x: 300, y: 100 }, data: { label: "TON", inTypes: ["BOOL"], outTypes: ["BOOL"] }, type: "default" },
  { id: "out1", position: { x: 550, y: 120 }, data: { label: "OUT BOOL", portType: "BOOL", direction: "in" }, type: "output" }
];

const initialEdges: Edge[] = [
];

type VarDef = { name: string; type: string; address?: string };
type PortDef = { name: string; type: string };
type BlockDef = { id: string; label: string; inPorts: PortDef[]; outPorts: PortDef[] };

const blockLibrary: BlockDef[] = [
  { id: "TON", label: "TON", inPorts: [{ name: "IN", type: "BOOL" }], outPorts: [{ name: "Q", type: "BOOL" }] },
  { id: "TOF", label: "TOF", inPorts: [{ name: "IN", type: "BOOL" }], outPorts: [{ name: "Q", type: "BOOL" }] },
  { id: "TP", label: "TP", inPorts: [{ name: "IN", type: "BOOL" }], outPorts: [{ name: "Q", type: "BOOL" }] },
  { id: "CTU", label: "CTU", inPorts: [{ name: "CU", type: "BOOL" }], outPorts: [{ name: "CV", type: "INT" }] },
  { id: "CTD", label: "CTD", inPorts: [{ name: "CD", type: "BOOL" }], outPorts: [{ name: "CV", type: "INT" }] },
  { id: "RS", label: "RS", inPorts: [{ name: "S", type: "BOOL" }, { name: "R", type: "BOOL" }], outPorts: [{ name: "Q", type: "BOOL" }] },
  { id: "SR", label: "SR", inPorts: [{ name: "S", type: "BOOL" }, { name: "R", type: "BOOL" }], outPorts: [{ name: "Q", type: "BOOL" }] }
];

export default function FbdEditor({ currentPou }: { currentPou?: string }) {
  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);
  const historyPast = React.useRef<{ nodes: Node[]; edges: Edge[] }[]>([]);
  const historyFuture = React.useRef<{ nodes: Node[]; edges: Edge[] }[]>([]);
  const mutationFlag = React.useRef(false);
  const [vars, setVars] = useState<VarDef[]>([]);
  const [vname, setVname] = useState("");
  const [vtype, setVtype] = useState("BOOL");
  const [vaddr, setVaddr] = useState("");
  const [addrValid, setAddrValid] = useState<boolean | null>(null);
  const [vdirection, setVdirection] = useState<"in"|"out">("out");
  const baseUrl = "http://localhost:5000";
  const nodeTypes = useMemo(() => ({ default: BlockNode as any, input: VarNode as any, output: VarNode as any }), []);
  const [quickOpen, setQuickOpen] = useState(false);
  const [quickText, setQuickText] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selectedNode = nodes.find(n => n.id === selectedId);
  const lastPouRef = React.useRef<string | undefined>(undefined);
  const saveTimer = React.useRef<number | null>(null);
  const onConnect = useCallback((c: Connection) => {
    const src = nodes.find(n => n.id === c.source);
    const tgt = nodes.find(n => n.id === c.target);
    if (!src || !tgt) return;
    const ok = validateByHandle(src, c.sourceHandle!, tgt, c.targetHandle!);
    if (!ok) return;
    setEdges((eds) => addEdge(c, eds));
    mutationFlag.current = true;
  }, [setEdges, nodes]);

  const onNodesChangeWrapped = useCallback((changes: any) => { onNodesChange(changes); mutationFlag.current = true; }, [onNodesChange]);
  const onEdgesChangeWrapped = useCallback((changes: any) => { onEdgesChange(changes); mutationFlag.current = true; }, [onEdgesChange]);

  const pushHistory = useCallback(() => {
    historyPast.current.push({ nodes: JSON.parse(JSON.stringify(nodes)), edges: JSON.parse(JSON.stringify(edges)) });
    if (historyPast.current.length > 200) historyPast.current.shift();
    historyFuture.current = { current: [] } as any; // reset future
  }, [nodes, edges]);

  useEffect(() => {
    if (mutationFlag.current) { pushHistory(); mutationFlag.current = false; }
  }, [nodes, edges, pushHistory]);
  const fitViewOptions = useMemo(() => ({ padding: 0.2 }), []);
  useEffect(() => {
    if (!currentPou) return;
    fetch(`${baseUrl}/pous/vars?name=${encodeURIComponent(currentPou)}`).then(r => r.json()).then(setVars).catch(() => {});
  }, [currentPou]);
  useEffect(() => {
    if (!currentPou) return;
    (async () => {
      const prev = lastPouRef.current;
      if (prev) {
        const payload = JSON.stringify({ nodes, edges });
        await fetch(`${baseUrl}/pous/canvas/save?name=${encodeURIComponent(prev)}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: payload }).catch(() => {});
      }
      const res = await fetch(`${baseUrl}/pous/canvas?name=${encodeURIComponent(currentPou)}`);
      if (res.ok) {
        const { nodes: ns, edges: es } = await res.json();
        setNodes(ns);
        setEdges(es);
      } else {
        setNodes(initialNodes);
        setEdges(initialEdges);
      }
      fetch(`${baseUrl}/pous/vars?name=${encodeURIComponent(currentPou)}`).then(r => r.json()).then(setVars).catch(() => {});
      lastPouRef.current = currentPou;
    })();
  }, [currentPou]);

  useEffect(() => {
    if (!currentPou) return;
    if (saveTimer.current) window.clearTimeout(saveTimer.current);
    saveTimer.current = window.setTimeout(async () => {
      const payload = JSON.stringify({ nodes, edges });
      await fetch(`${baseUrl}/pous/canvas/save?name=${encodeURIComponent(currentPou)}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: payload }).catch(() => {});
    }, 800);
    return () => { if (saveTimer.current) window.clearTimeout(saveTimer.current); };
  }, [nodes, edges, currentPou]);
  const addBlock = (def: BlockDef, name?: string) => {
    const id = `${name ?? def.id}-${Date.now()}`;
    setNodes(ns => ns.concat([{ id, position: { x: 300, y: 60 }, data: { label: name ?? def.label, inPorts: def.inPorts, outPorts: def.outPorts }, type: "default" }]));
  };
  const addVariable = async () => {
    if (!currentPou) return;
    const domain = vdirection === "out" ? "input" : "output";
    const payload = new URLSearchParams({ name: currentPou!, vname, type: vtype, address: vaddr, domain }).toString();
    await fetch(`${baseUrl}/pous/vars/upsert?${payload}`, { method: "POST" });
    const nodeId = `${vname}-${Date.now()}`;
    const label = `${vname}:${vtype}${vaddr ? `@${vaddr}` : ""}`;
    setNodes(ns => ns.concat([{ id: nodeId, position: { x: 60, y: 60 }, data: { label, portType: vtype, direction: vdirection, address: vaddr }, type: vdirection === "out" ? "input" : "output" }]));
    const list = await fetch(`${baseUrl}/pous/vars?name=${encodeURIComponent(currentPou)}`).then(r => r.json()).catch(() => []);
    setVars(list);
    setVname("");
    setVaddr("");
  };
  const handleQuickAdd = async () => {
    const txt = quickText.trim();
    const varDef = parseVar(txt);
    if (varDef) {
      if (!currentPou) return;
      const domain = varDef.dir === "out" ? "input" : "output";
      const payload = new URLSearchParams({ name: currentPou!, vname: varDef.name, type: varDef.type, address: varDef.address ?? "", domain }).toString();
      await fetch(`${baseUrl}/pous/vars/upsert?${payload}`, { method: "POST" });
      const nodeId = `${varDef.name}-${Date.now()}`;
      const label = `${varDef.name}:${varDef.type}${varDef.address ? `@${varDef.address}` : ""}`;
      setNodes(ns => ns.concat([{ id: nodeId, position: { x: 100, y: 100 }, data: { label, portType: varDef.type, direction: varDef.dir, address: varDef.address }, type: varDef.dir === "out" ? "input" : "output" }]));
      setQuickOpen(false);
      setQuickText("");
      return;
    }
    const blkDef = parseBlock(txt);
    if (blkDef) {
      const blk = blockLibrary.find(b => b.id.toLowerCase() === blkDef.id.toLowerCase() || b.label.toLowerCase() === blkDef.id.toLowerCase());
      if (blk) addBlock(blk, blkDef.name);
      setQuickOpen(false);
      setQuickText("");
    }
  };
  return (
    <div style={{ display: "flex", height: "100%" }} onKeyDown={(e) => {
      if (e.key === "/") { setQuickOpen(true); setQuickText(""); }
      if (quickOpen && e.key === "Enter") { handleQuickAdd(); }
    }} tabIndex={0}>
      <aside style={{ width: 260, borderRight: "1px solid #ddd", padding: 8 }}>
        <div style={{ fontWeight: 600, marginBottom: 8 }}>块库</div>
        {blockLibrary.map(b => (
          <div key={b.id} style={{ display: "flex", alignItems: "center", marginBottom: 6 }}>
            <span style={{ flex: 1 }}>{b.label}</span>
            <button onClick={() => addBlock(b)}>添加</button>
          </div>
        ))}
        <div style={{ fontWeight: 600, margin: "12px 0 8px" }}>变量</div>
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
          <select value={vdirection} onChange={e => setVdirection(e.target.value as any)}>
            <option value="out">输入节点</option>
            <option value="in">输出节点</option>
          </select>
          <button onClick={addVariable}>添加变量节点</button>
        </div>
        <div style={{ marginTop: 8 }}>
          {vars.map(v => (
            <div key={v.name}>{v.name}:{v.type}{v.address ? `@${v.address}` : ""}</div>
          ))}
        </div>
      </aside>
      <div style={{ flex: 1 }}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={onNodesChangeWrapped}
          onEdgesChange={onEdgesChangeWrapped}
          onConnect={onConnect}
          onNodeClick={(_, n) => setSelectedId(n.id)}
          fitViewOptions={fitViewOptions}
          fitView
          nodeTypes={nodeTypes}
        >
          <MiniMap />
          <Controls />
          <Background variant="dots" gap={12} size={1} />
        </ReactFlow>
        <div style={{ position: "absolute", right: 20, top: 20, display: "flex", gap: 8 }}>
          <button onClick={() => {
            const prev = historyPast.current.pop();
            if (!prev) return;
            historyFuture.current.push({ nodes, edges });
            setNodes(prev.nodes); setEdges(prev.edges);
          }}>撤销</button>
          <button onClick={() => {
            const next = historyFuture.current.pop();
            if (!next) return;
            historyPast.current.push({ nodes, edges });
            setNodes(next.nodes); setEdges(next.edges);
          }}>重做</button>
          <button onClick={async () => {
            if (!currentPou) return;
            const payload = JSON.stringify({ nodes, edges });
            await fetch(`${baseUrl}/pous/canvas/save?name=${encodeURIComponent(currentPou)}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: payload });
          }}>保存图</button>
          <button onClick={async () => {
            if (!currentPou) return;
            const res = await fetch(`${baseUrl}/pous/canvas?name=${encodeURIComponent(currentPou)}`);
            if (res.ok) {
              const { nodes: ns, edges: es } = await res.json();
              setNodes(ns);
              setEdges(es);
            }
          }}>加载图</button>
          <button onClick={async () => {
            if (!currentPou) return;
            const res = await fetch(`${baseUrl}/export/pou?name=${encodeURIComponent(currentPou)}`);
            const xml = await res.text();
            const blob = new Blob([xml], { type: "application/xml" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url; a.download = `${currentPou}.xml`; a.click(); URL.revokeObjectURL(url);
          }}>导出POU</button>
          <button onClick={async () => {
            if (!currentPou) return;
            const res = await fetch(`${baseUrl}/pous/st?name=${encodeURIComponent(currentPou)}`);
            const st = await res.text();
            const blob = new Blob([st], { type: "text/plain" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url; a.download = `${currentPou}.st`; a.click(); URL.revokeObjectURL(url);
          }}>导出ST</button>
          <label style={{ border: "1px solid #ccc", padding: "2px 6px", cursor: "pointer" }}>
            导入POU
            <input type="file" accept=".xml" style={{ display: "none" }} onChange={async (e) => {
              const f = e.target.files?.[0];
              if (!f) return;
              const txt = await f.text();
              const res = await fetch(`${baseUrl}/import/pou`, { method: "POST", headers: { "Content-Type": "application/xml" }, body: txt });
              if (res.ok) {
                const { nodes: ns, edges: es } = await res.json();
                const merge = window.confirm("是否合并到当前画布？取消将覆盖当前画布。");
                if (merge) {
                  setNodes(cur => (cur as any).concat(ns as any));
                  setEdges(cur => (cur as any).concat(es as any));
                } else {
                  setNodes(ns as any);
                  setEdges(es as any);
                }
                if (currentPou) {
                  const payload = JSON.stringify({ nodes: ns, edges: es });
                  await fetch(`${baseUrl}/pous/canvas/save?name=${encodeURIComponent(currentPou)}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: payload });
                }
              }
              e.currentTarget.value = "";
            }} />
          </label>
        </div>
        {quickOpen && (
          <div style={{ position: "absolute", left: 300, top: 20, background: "#fff", border: "1px solid #ccc", padding: 8 }}>
            <input autoFocus value={quickText} onChange={e => setQuickText(e.target.value)} placeholder="输入块名或 var name:type@addr in|out" />
            <button onClick={handleQuickAdd}>添加</button>
            <button onClick={() => setQuickOpen(false)}>关闭</button>
          </div>
        )}
        {selectedNode && (
          <div style={{ position: "absolute", right: 20, bottom: 20, background: "#fff", border: "1px solid #ccc", padding: 10, width: 260 }}>
            <div style={{ fontWeight: 600, marginBottom: 6 }}>属性</div>
            <div>节点ID：{selectedNode.id}</div>
            <div style={{ marginTop: 6 }}>
              <label>标签</label>
              <input value={(selectedNode.data as any).label ?? ""} onChange={e => {
                const val = e.target.value;
                setNodes(ns => ns.map(n => n.id === selectedNode.id ? { ...n, data: { ...n.data, label: val } } : n));
              }} />
            </div>
            {selectedNode.type === "default" && (
              <div style={{ marginTop: 6 }}>
                <label>参数(JSON)</label>
                <textarea rows={4} value={JSON.stringify((selectedNode.data as any).params ?? {})} onChange={e => {
                  try {
                    const obj = JSON.parse(e.target.value || "{}");
                    setNodes(ns => ns.map(n => n.id === selectedNode.id ? { ...n, data: { ...n.data, params: obj } } : n));
                  } catch {}
                }} />
              </div>
            )}
            {selectedNode.type === "default" && (
              <div style={{ marginTop: 6 }}>
                {renderStructuredParams(selectedNode, setNodes)}
              </div>
            )}
            {selectedNode.type === "default" && (
              <div style={{ marginTop: 6 }}>
                <div style={{ fontWeight: 600 }}>端口绑定</div>
                {((selectedNode.data as any).inPorts ?? []).map((p: any) => {
                  const bindName = ((selectedNode.data as any).bindings?.[p.name]) ?? "";
                  const candidates = vars.filter(x => x.type === p.type);
                  return (
                    <div key={p.name} style={{ display: "flex", gap: 6, alignItems: "center" }}>
                      <span style={{ minWidth: 80 }}>{p.name}:{p.type}</span>
                      <select value={bindName} onChange={e => {
                        const val = e.target.value;
                        setNodes(ns => ns.map(n => n.id === selectedNode.id ? { ...n, data: { ...n.data, bindings: { ...(n.data as any).bindings, [p.name]: val } } } : n));
                      }}>
                        <option value="">未绑定</option>
                        {candidates.map(v => (
                          <option key={`${v.name}-${v.type}`} value={v.name}>{`${v.name}:${v.type}${v.address ? `@${v.address}` : ""}`}</option>
                        ))}
                      </select>
                      <button onClick={() => {
                        setNodes(ns => ns.map(n => n.id === selectedNode.id ? { ...n, data: { ...n.data, bindings: { ...(n.data as any).bindings, [p.name]: "" } } } : n));
                      }}>清除</button>
                    </div>
                  );
                })}
                <datalist id="varlist">
                  {vars.map(v => (<option key={v.name} value={v.name} />))}
                </datalist>
              </div>
            )}
            <div style={{ display: "flex", gap: 8, marginTop: 8 }}>
              <button onClick={() => setSelectedId(null)}>关闭</button>
              <button onClick={() => {
                setEdges(es => es.filter(e => e.source !== selectedNode.id && e.target !== selectedNode.id));
                setNodes(ns => ns.filter(n => n.id !== selectedNode.id));
                setSelectedId(null);
              }}>删除节点</button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function parseVar(input: string): { name: string; type: string; address?: string; dir: "in"|"out" } | null {
  const m = input.match(/^var\s+(\w+):(\w+)(?:@([^\s]+))?\s+(in|out)$/i);
  if (!m) return null;
  return { name: m[1], type: m[2], address: m[3], dir: m[4] as any };
}

function parseBlock(input: string): { id: string; name?: string } | null {
  const m = input.match(/^(\w+)(?:\s+(\w+))?$/);
  if (!m) return null;
  return { id: m[1], name: m[2] };
}

function renderStructuredParams(node: Node, setNodes: any) {
  const data: any = node.data;
  const id = (data.label || "").toUpperCase();
  const params = data.params ?? {};
  if (["TON", "TOF", "TP"].includes(id)) {
    const pt = params.PT ?? 1000;
    return (
      <div>
        <div>PT(ms)</div>
        <input type="number" value={pt} onChange={e => {
          const v = parseInt(e.target.value);
          setNodes((ns: Node[]) => ns.map(n => n.id === node.id ? { ...n, data: { ...n.data, params: { ...params, PT: v } } } : n));
        }} />
      </div>
    );
  }
  if (["CTU", "CTD"].includes(id)) {
    const pv = params.PV ?? 10;
    return (
      <div>
        <div>PV</div>
        <input type="number" value={pv} onChange={e => {
          const v = parseInt(e.target.value);
          setNodes((ns: Node[]) => ns.map(n => n.id === node.id ? { ...n, data: { ...n.data, params: { ...params, PV: v } } } : n));
        }} />
      </div>
    );
  }
  return null;
}

function validateByHandle(src: Node, sHandle: string, tgt: Node, tHandle: string): boolean {
  const sData: any = src.data; const tData: any = tgt.data;
  const sType = src.type === "default" ? sData.outPorts?.find((p: any) => `out:${p.name}` === sHandle)?.type : sData.portType;
  const tType = tgt.type === "default" ? tData.inPorts?.find((p: any) => `in:${p.name}` === tHandle)?.type : tData.portType;
  return !!sType && !!tType && sType === tType;
}
