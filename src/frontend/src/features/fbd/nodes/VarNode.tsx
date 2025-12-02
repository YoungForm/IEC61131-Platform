import React from "react";
import { Handle, Position } from "reactflow";

export default function VarNode({ data }: any) {
  const dir = data.direction;
  const id = dir === "in" ? "in:VAR" : "out:VAR";
  return (
    <div style={{ padding: 8, border: "1px solid #aaa", borderRadius: 4, background: "#f7f7f7" }}>
      <div>{data.label} {data.direction === "out" ? "[输入]" : "[输出]"}</div>
      {dir === "in" ? <Handle type="target" position={Position.Left} id={id} /> : <Handle type="source" position={Position.Right} id={id} />}
    </div>
  );
}
