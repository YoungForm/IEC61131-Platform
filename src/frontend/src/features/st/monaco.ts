import * as monaco from "monaco-editor";

export function registerStLanguage() {
  monaco.languages.register({ id: "st" });
  monaco.languages.setMonarchTokensProvider("st", {
    keywords: [
      "PROGRAM","FUNCTION","FUNCTION_BLOCK","VAR","VAR_INPUT","VAR_OUTPUT","VAR_IN_OUT","END_VAR",
      "IF","THEN","ELSIF","ELSE","END_IF","CASE","OF","END_CASE","FOR","TO","BY","DO","END_FOR",
      "WHILE","END_WHILE","REPEAT","UNTIL","END_REPEAT","RETURN","TRUE","FALSE"
    ],
    operators: [":=","+","-","*","/","<","<=",">",">=","=","<>","AND","OR","NOT"],
    tokenizer: {
      root: [
        [/\b[0-9]+\b/, "number"],
        [/\b(TRUE|FALSE)\b/i, "boolean"],
        [/\b([A-Z_][A-Z0-9_]*)\b/i, {
          cases: { "@keywords": "keyword", "@default": "identifier" }
        }],
        [/\(\*.*?\*\)/, "comment"],
        [/"[^"]*"/, "string"],
        [/[:=+\-*/<>]/, "operator"]
      ]
    }
  });
  monaco.languages.setLanguageConfiguration("st", {
    comments: { blockComment: ["(*", "*)"] },
    brackets: [
      ["(", ")"],
      ["[", "]"]
    ]
  });
}

