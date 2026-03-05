using System;
using System.Collections.Generic;
using System.Text;
using FGDDumper;

namespace WikiPageTools;

public static class CSScriptTSParser
{
    public static bool ParseScriptFile(string filePath)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            string[] allLines = File.ReadAllLines(filePath);

            // ── Per-method state ──────────────────────────────────────────────────────
            string funcDescription = "";

            // Multi-line comment accumulation
            bool inBlockComment = false;
            List<string> commentLines = new();

            // ── Per-class state ───────────────────────────────────────────────────────
            string? currentClass = null;
            string? currentClassDesc = null;
            int braceDepth = 0;
            bool inClassBody = false;

            // className → list of method row strings
            var classTables = new Dictionary<string, List<string>>();
            // className → list of event row strings
            var eventTables = new Dictionary<string, List<string>>();
            var classDescriptions = new Dictionary<string, string>();

            // Collected type aliases:  (name, definition, description)
            var typeAliases = new List<(string Name, string Definition, string Description)>();

            // Collected interfaces:  name → (description, raw body lines)
            var interfaces = new Dictionary<string, (string Description, List<string> BodyLines)>();
            string? currentInterface = null;
            string? currentInterfaceDesc = null;
            int interfaceBraceDepth = 0;

            // Collected enums:  name → (description, list of (member, optional value, member comment))
            var enums = new Dictionary<string, (string Description, List<(string Member, string? Value, string Comment)> Members)>();
            string? currentEnum = null;
            string? currentEnumDesc = null;
            string pendingMemberComment = "";

            // ── Helpers ───────────────────────────────────────────────────────────────

            bool IsMethodDeclaration(string line)
            {
                if (!line.Contains("(")) return false;

                string[] skipKeywords = { "if ", "else ", "foreach ", "for ", "while ", "switch ", "catch ", "using ", "export " };
                foreach (var kw in skipKeywords)
                    if (line.StartsWith(kw)) return false;

                // Top-level arrow type shapes are NOT method declarations:
                //   type Foo = (x: T) => R;
                // BUT methods that TAKE a callback ARE valid:
                //   OnActivate(callback: () => void): void;
                // The difference: a real method has its own '(' before any '=>'.
                if (line.Contains("=>"))
                {
                    var firstParen = line.IndexOf('(');
                    var firstArrow = line.IndexOf("=>");
                    if (firstArrow < firstParen) return false;
                }

                // TypeScript declarations end with ';'
                if (!line.TrimEnd().EndsWith(";")) return false;

                if (line.StartsWith("//")) return false;

                return true;
            }

            // Event methods take a callback parameter — they register listeners, not compute values
            bool IsEventMethod(string name, string signature)
            {
                return signature.Contains("callback:") || name.StartsWith("On");
            }

            bool TryParseClassDeclaration(string line, out string className)
            {
                className = "";
                if (line.TrimStart().StartsWith("//")) return false;

                var idx = line.IndexOf("class ");
                if (idx == -1) return false;

                var rest = line.Substring(idx + 6).Trim();
                int end = 0;
                while (end < rest.Length && rest[end] != ' ' && rest[end] != '<' && rest[end] != '{')
                    end++;

                className = rest.Substring(0, end).Trim();
                return !string.IsNullOrEmpty(className);
            }

            void EnsureClassExists(string name)
            {
                if (!classTables.ContainsKey(name))
                    classTables[name] = new List<string>();
                if (!eventTables.ContainsKey(name))
                    eventTables[name] = new List<string>();
            }

            bool TryParseInterfaceDeclaration(string line, out string ifName)
            {
                ifName = "";
                if (line.TrimStart().StartsWith("//")) return false;
                var idx = line.IndexOf("interface ");
                if (idx == -1) return false;
                var rest = line.Substring(idx + 10).Trim();
                int end = 0;
                while (end < rest.Length && rest[end] != ' ' && rest[end] != '<' && rest[end] != '{')
                    end++;
                ifName = rest.Substring(0, end).Trim();
                return !string.IsNullOrEmpty(ifName);
            }

            bool TryParseEnumDeclaration(string line, out string enumName)
            {
                enumName = "";
                if (line.TrimStart().StartsWith("//")) return false;
                var idx = line.IndexOf("enum ");
                if (idx == -1) return false;
                var rest = line.Substring(idx + 5).Trim();
                int end = 0;
                while (end < rest.Length && rest[end] != ' ' && rest[end] != '<' && rest[end] != '{')
                    end++;
                enumName = rest.Substring(0, end).Trim();
                return !string.IsNullOrEmpty(enumName);
            }

            void EmitRow(string name, string signature, string description)
            {
                var tags = new List<string>();
                var tagStr = tags.Count > 0 ? $" *({string.Join(", ", tags)})*" : "";

                var target = currentClass ?? "__toplevel__";
                EnsureClassExists(target);

                var stored = $"{name}\x00{signature}\x00{description}{tagStr}";

                // Route to event table or method table
                if (IsEventMethod(name, signature))
                    eventTables[target].Add(stored);
                else
                    classTables[target].Add(stored);
            }

            // ── Main parse loop ───────────────────────────────────────────────────────

            foreach (var line in allLines)
            {
                var trimmedLine = line.Trim();

                // ── Interface body (raw capture — must come before comment handlers) ───
                if (currentInterface != null)
                {
                    interfaceBraceDepth += trimmedLine.Count(c => c == '{') - trimmedLine.Count(c => c == '}');

                    if (interfaceBraceDepth <= 0)
                    {
                        currentInterface = null;
                        interfaceBraceDepth = 0;
                        funcDescription = "";
                    }
                    else
                    {
                        interfaces[currentInterface].BodyLines.Add(line.TrimEnd());
                    }
                    continue;
                }

                // ── Block comment open ────────────────────────────────────────────────
                if (trimmedLine.StartsWith("/**") && !trimmedLine.Contains("*/"))
                {
                    inBlockComment = true;
                    commentLines.Clear();

                    var afterOpen = trimmedLine.Substring(3).Trim();
                    if (!string.IsNullOrWhiteSpace(afterOpen))
                        commentLines.Add(afterOpen);
                    continue;
                }

                // ── Inside block comment ──────────────────────────────────────────────
                if (inBlockComment)
                {
                    if (trimmedLine.Contains("*/"))
                    {
                        inBlockComment = false;

                        var beforeClose = trimmedLine.Substring(0, trimmedLine.IndexOf("*/")).TrimStart('*').Trim();
                        if (!string.IsNullOrWhiteSpace(beforeClose))
                            commentLines.Add(beforeClose);

                        var descParts = commentLines
                            .Select(l => l.TrimStart('*').Trim())
                            .Where(l => !string.IsNullOrWhiteSpace(l)
                                     && !l.StartsWith("@param")
                                     && !l.StartsWith("@returns")
                                     && !l.StartsWith("@example"));

                        funcDescription = string.Join("\n\n", descParts).Trim();
                        continue;
                    }

                    var interior = trimmedLine.TrimStart('*').Trim();
                    if (!string.IsNullOrWhiteSpace(interior))
                        commentLines.Add(interior);
                    continue;
                }

                // ── Single-line /** ... */ comment ───────────────────────────────────
                {
                    var si = trimmedLine.IndexOf("/**");
                    var ei = trimmedLine.IndexOf("*/");

                    if (si != -1 && ei != -1 && ei > si)
                    {
                        funcDescription = trimmedLine.Substring(si + 3, ei - (si + 3)).Trim();
                        continue;
                    }
                }

                // ── Interface declaration ─────────────────────────────────────────────
                if (!inClassBody && currentEnum == null
                    && TryParseInterfaceDeclaration(trimmedLine, out var parsedIfName))
                {
                    currentInterface = parsedIfName;
                    currentInterfaceDesc = funcDescription;
                    interfaceBraceDepth = trimmedLine.Count(c => c == '{') - trimmedLine.Count(c => c == '}');
                    interfaces[currentInterface] = (currentInterfaceDesc, new List<string>());

                    funcDescription = "";
                    continue;
                }

                // ── Enum declaration ──────────────────────────────────────────────────
                if (!inClassBody && currentEnum == null
                    && TryParseEnumDeclaration(trimmedLine, out var parsedEnumName))
                {
                    currentEnum = parsedEnumName;
                    currentEnumDesc = funcDescription;
                    enums[currentEnum] = (currentEnumDesc, new List<(string, string?, string)>());

                    funcDescription = "";
                    pendingMemberComment = "";
                    continue;
                }

                // ── Inside enum body ──────────────────────────────────────────────────
                if (currentEnum != null)
                {
                    if (trimmedLine == "}" || trimmedLine == "},")
                    {
                        currentEnum = null;
                        pendingMemberComment = "";
                        funcDescription = "";
                        continue;
                    }

                    var inlineSi = trimmedLine.IndexOf("/**");
                    var inlineEi = trimmedLine.IndexOf("*/");
                    if (inlineSi != -1 && inlineEi != -1 && inlineEi > inlineSi)
                    {
                        pendingMemberComment = trimmedLine.Substring(inlineSi + 3, inlineEi - (inlineSi + 3)).Trim();
                        var afterComment = trimmedLine.Substring(inlineEi + 2).Trim().TrimEnd(',');
                        if (!string.IsNullOrWhiteSpace(afterComment))
                        {
                            var eqI = afterComment.IndexOf('=');
                            var mName = eqI != -1 ? afterComment.Substring(0, eqI).Trim() : afterComment.Trim();
                            var mVal = eqI != -1 ? afterComment.Substring(eqI + 1).Trim() : (string?)null;
                            enums[currentEnum].Members.Add((mName, mVal, pendingMemberComment));
                            pendingMemberComment = "";
                        }
                        continue;
                    }

                    var memberLine = trimmedLine.TrimEnd(',');
                    if (!string.IsNullOrWhiteSpace(memberLine) && !memberLine.StartsWith("//") && memberLine != "{")
                    {
                        var eqIdx = memberLine.IndexOf('=');
                        var mName = eqIdx != -1 ? memberLine.Substring(0, eqIdx).Trim() : memberLine.Trim();
                        var mValue = eqIdx != -1 ? memberLine.Substring(eqIdx + 1).Trim() : (string?)null;
                        enums[currentEnum].Members.Add((mName, mValue, pendingMemberComment));
                        pendingMemberComment = "";
                    }
                    continue;
                }

                // ── Class declaration ─────────────────────────────────────────────────
                if (TryParseClassDeclaration(trimmedLine, out var parsedClassName))
                {
                    currentClass = parsedClassName;
                    currentClassDesc = funcDescription;
                    inClassBody = true;
                    braceDepth = 0;

                    EnsureClassExists(currentClass);
                    if (!string.IsNullOrEmpty(currentClassDesc))
                        classDescriptions[currentClass] = currentClassDesc;

                    funcDescription = "";

                    braceDepth += trimmedLine.Count(c => c == '{');
                    braceDepth -= trimmedLine.Count(c => c == '}');
                    continue;
                }

                // ── Track brace depth to detect end of class body ─────────────────────
                if (inClassBody && currentClass != null)
                {
                    braceDepth += trimmedLine.Count(c => c == '{');
                    braceDepth -= trimmedLine.Count(c => c == '}');

                    if (braceDepth <= 0)
                    {
                        currentClass = null;
                        inClassBody = false;
                        braceDepth = 0;
                        funcDescription = "";
                        continue;
                    }
                }

                // ── Type alias ────────────────────────────────────────────────────────
                if (!inClassBody && (trimmedLine.StartsWith("type ") || trimmedLine.StartsWith("export type ")))
                {
                    var typeDecl = trimmedLine.StartsWith("export ") ? trimmedLine.Substring(7) : trimmedLine;
                    var eqIdx = typeDecl.IndexOf('=');
                    if (eqIdx != -1)
                    {
                        var typeName = typeDecl.Substring(5, eqIdx - 5).Trim();
                        var typeDefinition = typeDecl.Substring(eqIdx + 1).TrimEnd(';').Trim();
                        typeAliases.Add((typeName, typeDefinition, funcDescription));
                    }

                    funcDescription = "";
                    continue;
                }

                // ── Method declaration ────────────────────────────────────────────────
                if (IsMethodDeclaration(trimmedLine))
                {
                    var funcSignature = trimmedLine.TrimEnd(';');
                    var funcName = trimmedLine.Substring(0, trimmedLine.IndexOf("(")).Trim();

                    EmitRow(funcName, funcSignature, funcDescription);

                    funcDescription = "";
                    continue;
                }

                // ── Any other non-blank, non-comment line resets pending description ──
                if (!string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("//"))
                {
                    funcDescription = "";
                }
            }

            // ── Build output ──────────────────────────────────────────────────────────

            var knownTypeNames = new HashSet<string>();
            foreach (var (n, _, _) in typeAliases) knownTypeNames.Add(n);
            foreach (var (n, _) in interfaces) knownTypeNames.Add(n);
            foreach (var (n, _) in enums) knownTypeNames.Add(n);
            foreach (var n in classTables.Keys)
                if (n != "__toplevel__") knownTypeNames.Add(n);

            string LinkTypes(string sanitised)
            {
                foreach (var typeName in knownTypeNames.OrderByDescending(t => t.Length))
                {
                    var anchor = typeName.ToLowerInvariant();
                    var link = $"[{typeName}](#{anchor})";
                    sanitised = System.Text.RegularExpressions.Regex.Replace(
                        sanitised,
                        $@"(?<!\[)(?<!\(#)\b{System.Text.RegularExpressions.Regex.Escape(typeName)}\b(?!\])",
                        link);
                }
                return sanitised;
            }

            string RenderRow(string stored)
            {
                var parts = stored.Split('\x00');
                var rName = SanitizeExtra(parts[0]);
                var rSig = LinkTypes(SanitizeExtra(parts[1]));
                var rDesc = LinkTypes(SanitizeExtra(parts.Length > 2 ? parts[2] : ""));
                return $"|{rName}|{rSig}|{rDesc}|";
            }

            // All class names that have any rows (methods or events)
            var allClassNames = classTables.Keys
                .Union(eventTables.Keys)
                .Distinct()
                .ToList();

            var output = new System.Text.StringBuilder();

            foreach (var className in allClassNames)
            {
                var hasMethods = classTables.TryGetValue(className, out var methodRows) && methodRows!.Count > 0;
                var hasEvents = eventTables.TryGetValue(className, out var eventRows) && eventRows!.Count > 0;

                if (!hasMethods && !hasEvents) continue;

                var displayName = className == "__toplevel__" ? "Module-level" : className;
                output.AppendLine($"### {displayName}");

                if (classDescriptions.TryGetValue(className, out var classDesc) && !string.IsNullOrEmpty(classDesc))
                    output.AppendLine(classDesc);

                output.AppendLine();

                // ── Methods sub-table ──────────────────────────────────────────────
                if (hasMethods)
                {
                    output.AppendLine("#### Functions");
                    output.AppendLine();
                    output.AppendLine("|Name|Signature|Description|");
                    output.AppendLine("|---|---|---|");

                    foreach (var row in methodRows!)
                        output.AppendLine(RenderRow(row));

                    output.AppendLine();
                }

                // ── Events sub-table ───────────────────────────────────────────────
                if (hasEvents)
                {
                    output.AppendLine("#### Events");
                    output.AppendLine();
                    output.AppendLine("|Name|Signature|Description|");
                    output.AppendLine("|---|---|---|");

                    foreach (var row in eventRows!)
                        output.AppendLine(RenderRow(row));

                    output.AppendLine();
                }
            }

            // ── Types section ─────────────────────────────────────────────────────────

            if (typeAliases.Count > 0)
            {
                output.AppendLine("## Types");
                output.AppendLine();

                foreach (var (typeName, typeDef, typeDesc) in typeAliases)
                {
                    output.AppendLine($"### {typeName}");

                    if (!string.IsNullOrEmpty(typeDesc))
                        output.AppendLine(typeDesc);

                    output.AppendLine();
                    output.AppendLine("```ts");
                    output.AppendLine($"type {typeName} = {typeDef};");
                    output.AppendLine("```");
                    output.AppendLine();
                }
            }

            // ── Interfaces section ────────────────────────────────────────────────────

            if (interfaces.Count > 0)
            {
                output.AppendLine("## Interfaces");
                output.AppendLine();

                foreach (var (ifName, (ifDesc, bodyLines)) in interfaces)
                {
                    output.AppendLine($"### {ifName}");

                    if (!string.IsNullOrEmpty(ifDesc))
                    {
                        output.AppendLine(ifDesc);
                        output.AppendLine();
                    }

                    output.AppendLine("```ts");
                    output.AppendLine($"interface {ifName} {{");
                    foreach (var bodyLine in bodyLines)
                        output.AppendLine("    " + bodyLine.Trim());
                    output.AppendLine("}");
                    output.AppendLine("```");
                    output.AppendLine();
                }
            }

            // ── Enums section ─────────────────────────────────────────────────────────

            if (enums.Count > 0)
            {
                output.AppendLine("## Enums");
                output.AppendLine();

                foreach (var (enumName, (enumDesc, members)) in enums)
                {
                    output.AppendLine($"### {enumName}");

                    if (!string.IsNullOrEmpty(enumDesc))
                    {
                        output.AppendLine(enumDesc);
                        output.AppendLine();
                    }

                    foreach (var (mName, mValue, mComment) in members)
                    {
                        var label = mValue != null ? $"{mName} = {mValue}" : mName;
                        output.AppendLine($"- {SanitizeExtra(label)}");
                    }

                    output.AppendLine();
                }
            }

            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(filePath)!, "cs_script_doc_output.txt"),
                output.ToString());
        }

        return true;

    }

    private static string SanitizeExtra(string input)
    {
        input = input.Replace("&", @"\&")
                     .Replace("<", @"\<")
                     .Replace(">", @"\>")
                     .Replace("|", @"\|");

        // Escape { and } only outside of backtick code spans.
        var result = new StringBuilder();
        bool inCode = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '`')
            {
                inCode = !inCode;
                result.Append(c);
            }
            else if (!inCode && c == '{')
            {
                result.Append(@"\{");
            }
            else if (!inCode && c == '}')
            {
                result.Append(@"\}");
            }
            else
            {
                result.Append(c);
            }
        }

        // Encode newlines as <br /> LAST, after all escaping, so the tag is never
        // touched by SanitizeTable's < and > replacements.
        return result.ToString()
            .Replace("\r\n", "<br />")
            .Replace("\r", "<br />")
            .Replace("\n", "<br />");
    }
}
