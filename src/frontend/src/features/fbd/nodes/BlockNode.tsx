import React from "react";
import { Handle, Position } from "reactflow";

export default function BlockNode({ data }: any) {
  const inPorts: { name: string; type: string }[] = data.inPorts ?? [];
  const outPorts: { name: string; type: string }[] = data.outPorts ?? [];
  return (
    <div style={{ padding: 8, border: "1px solid #aaa", borderRadius: 4, background: "#fff", minWidth: 160, position: "relative" }}>
      <div style={{ fontWeight: 600, marginBottom: 4 }}>{data.label}</div>
      {inPorts.map((p, idx) => (
        <div key={p.name} style={{ position: "absolute", left: -100, top: 24 + idx * 16, width: 90, textAlign: "right", fontSize: 12 }}>{p.name}:{p.type}</div>
      ))}
      {outPorts.map((p, idx) => (
        <div key={p.name} style={{ position: "absolute", right: -100, top: 24 + idx * 16, width: 90, textAlign: "left", fontSize: 12 }}>{p.name}:{p.type}</div>
      ))}
      {inPorts.map((p, idx) => (
        <Handle key={`in-${p.name}`} type="target" position={Position.Left} id={`in:${p.name}`} style={{ top: 24 + idx * 16 }} />
      ))}
      {outPorts.map((p, idx) => (
        <Handle key={`out-${p.name}`} type="source" position={Position.Right} id={`out:${p.name}`} style={{ top: 24 + idx * 16 }} />
      ))}
    </div>
  );
}
