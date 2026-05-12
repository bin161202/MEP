# Template prompts cho Antigravity MCP `send_code_to_revit` auto-test

3 prompt template member dùng để Antigravity tự test feature qua MCP server `send_code_to_revit` (open-source `chuongmep/revit-mcp` hoặc tương đương đã setup).

Workflow chung:
- Member sửa code feature, build sln, Reload qua RevitAddinManager
- Member test thủ công 1 case → OK
- Member dán prompt template phù hợp vào Antigravity, fill biến `{{...}}`
- Antigravity dùng MCP probe Revit, invoke command, probe lại, so sánh, báo PASS/FAIL từng scenario

---

## Prompt 1 — Probe-Invoke-Probe (CRUD pattern)

Dùng cho feature thay đổi state Revit (Rename, Move, SetParameter, CreateFamilyInstance).

```
Task: Auto-test feature {{FeatureName}} trong Revit qua MCP send_code_to_revit.

Scenarios:
1. {{Scenario1Description}}
   - Setup:    {{Scenario1Setup}}        (vd: select 5 element category Generic Models)
   - Input:    {{Scenario1Input}}        (vd: pattern "EQ-{i:D3}")
   - Expected: {{Scenario1Expected}}     (vd: 5 element có Mark = EQ-001..EQ-005)

2. {{Scenario2Description}}
   - Setup:    {{Scenario2Setup}}
   - Input:    {{Scenario2Input}}
   - Expected: {{Scenario2Expected}}

3. {{Scenario3Description}}
   - ...

Method per scenario:
  a. Probe trước: dump JSON các parameter relevant của element trong scope qua MCP
  b. Invoke command:
     - Load assembly: %LocalAppData%\MEPAuto\2024\MEPAuto.{{FeatureName}}.dll
     - Type: MEPAuto.{{FeatureName}}.Commands.{{FeatureName}}Command
     - Activator.CreateInstance + cast IExternalCommand
     - cmd.Execute(commandData, ref msg, new ElementSet())
  c. Probe sau: dump lại cùng parameter
  d. So sánh: assert Expected matches actual diff (probe sau − probe trước)

Output format: 1 dòng per scenario "[PASS/FAIL] {{ScenarioN}}: <details>". 
Nếu FAIL: in Element ID + parameter expected vs actual để debug nhanh.
```

---

## Prompt 2 — Rollback verification (transaction safety)

Dùng cho feature có transaction modify nhiều element cùng lúc — verify rollback nguyên vẹn khi 1 step lỗi.

```
Task: Verify {{FeatureName}} rollback khi exception giữa transaction qua MCP.

Setup: {{Setup}}    (vd: chuẩn bị document có 10 pipe + 1 pipe có invalid SystemType)

Scenario: invoke {{FeatureName}}Command → expected exception ở element thứ N → toàn bộ transaction RollBack.

Method:
  a. Probe trước: dump element count + version document.
     int beforeCount = doc.GetElements().Count();
     long beforeVersion = doc.GetEditTransactionFinalVersion();
  b. Invoke command (expect throws / Result.Failed).
  c. Probe sau: dump cùng metric.
  d. Assert:
     - beforeCount == afterCount  (không có orphan element)
     - Document title KHÔNG có dấu "*" (modified)
     - TaskDialog/message có chứa text lỗi rõ ràng

Output: PASS nếu tất cả assert đúng. FAIL + dump diff nếu sai.
Edge: nếu feature dùng IRevitService.RunInTransaction (best practice), rollback
được handle tự động → test này verify wrapper hoạt động đúng.
```

---

## Prompt 3 — Multi-version compatibility (Revit 2024 vs 2025)

Dùng để verify ElementIdAdapter pattern không leak version-specific behavior. Yêu cầu: mở cả Revit 2024 + 2025 cùng lúc với cùng document mẫu.

```
Task: So sánh hành vi feature {{FeatureName}} giữa Revit 2024 (net48, REVIT_INT_ID) 
vs Revit 2025 (net8, REVIT_LONG_ID) qua MCP.

Setup:
  - Revit 2024 đã mở document mẫu shared/test-fixtures/{{FeatureName}}-test.rvt
  - Revit 2025 đã mở CÙNG document đó (mở 2 instance Revit khác nhau)
  - Cài DLL MEPAuto.{{FeatureName}}.dll cả Release-2024 + Release-2025 build
  - Selection + state document giữa 2 instance KHỚP nhau

Method per Revit instance:
  1. Probe trước (state document)
  2. Invoke {{FeatureName}}Command với cùng input
  3. Probe sau
  4. Trích xuất diff (created/modified/deleted elements + parameter changes)

Compare: 
  - Element count diff phải bằng nhau giữa 2 version
  - Parameter changes phải khớp (Mark, Comments, ...) — KHÔNG so element ID 
    raw vì khác kiểu int vs long, dùng category + position để match
  - Output message TaskDialog phải giống

Output: PASS nếu diff giống nhau. FAIL + side-by-side diff table nếu khác.
```

---

## Tips

- **Antigravity rate limit**: 3 scenario/prompt là sweet spot. Nhiều hơn → response timeout.
- **Element ID stability**: KHÔNG so element ID raw giữa 2 version (kiểu khác, có thể renumber). So qua category + bbox center + parameter Mark.
- **Khi MCP fail**: thường do connection MCP server/Revit chưa setup. Check process Revit + MCP server alive.
- **Cẩm nang chi tiết**: xem `docs/workflow/MEMBER-DEV-WORKFLOW.md`.
