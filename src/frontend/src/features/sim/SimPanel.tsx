import React, { useEffect, useState } from "react";
import { baseUrl } from "../../shared/config";

type Vars = Record<string, number>;

export default function SimPanel() {
  const [vars, setVars] = useState<Vars>({});
  const [periodMs, setPeriodMs] = useState(100);
  const [name, setName] = useState("x");
  const [value, setValue] = useState(0);
  const [timer, setTimer] = useState<number | null>(null);

  const base = baseUrl;

  const fetchVars = async () => {
    try {
      const res = await fetch(`${base}/sim/vars`);
      const data = await res.json();
      setVars(data);
    } catch {}
  };

  useEffect(() => {
    const id = window.setInterval(fetchVars, 500);
    setTimer(id);
    return () => { if (id) window.clearInterval(id); };
  }, []);

  return (
    <div style={{ padding: 12 }}>
      <div style={{ marginBottom: 8 }}>
        <button onClick={async () => { await fetch(`${base}/sim/start?periodMs=${periodMs}`, { method: "POST" }); }}>启动仿真</button>
        <button onClick={async () => { await fetch(`${base}/sim/stop`, { method: "POST" }); }}>停止仿真</button>
        <input type="number" value={periodMs} onChange={e => setPeriodMs(parseInt(e.target.value))} style={{ marginLeft: 8 }} /> ms
      </div>
      <div style={{ marginBottom: 8 }}>
        <input value={name} onChange={e => setName(e.target.value)} />
        <input type="number" value={value} onChange={e => setValue(parseFloat(e.target.value))} />
        <button onClick={async () => { await fetch(`${base}/sim/set?name=${encodeURIComponent(name)}&value=${value}`, { method: "POST" }); fetchVars(); }}>设置变量</button>
      </div>
      <table>
        <thead><tr><th>变量</th><th>值</th></tr></thead>
        <tbody>
          {Object.entries(vars).map(([k, v]) => (<tr key={k}><td>{k}</td><td>{v}</td></tr>))}
        </tbody>
      </table>
    </div>
  );
}

