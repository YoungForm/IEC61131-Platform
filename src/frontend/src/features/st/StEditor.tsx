import React, { useEffect, useRef } from "react";
import * as monaco from "monaco-editor";
import { registerStLanguage } from "./monaco";

export default function StEditor() {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    registerStLanguage();
    const editor = monaco.editor.create(ref.current!, {
      value: "PROGRAM Main\nVAR\n    x : INT;\nEND_VAR\nIF x > 0 THEN\n    x := x + 1;\nEND_IF\nEND_PROGRAM",
      language: "st",
      automaticLayout: true,
      theme: "vs"
    });
    return () => editor.dispose();
  }, []);
  return <div style={{ height: "100%" }} ref={ref} />;
}

