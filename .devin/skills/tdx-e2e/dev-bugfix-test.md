# dev-bugfix-test — Bug Fix Test Evidence Workflow

Companion content cho P3.6. Load khi P1.2 classify = bug và không có qa/staging access. Sinh mock unit test cho bug fix evidence.

## Condition

`if P1.2.type == "bug" && no qa/staging access` — chỉ trigger cho bug fix. Feature cần E2E (P5), refactor cần regression (P3r.2).

## Test project detection

1. `find_file_by_name` `*.Tests.csproj` hoặc `*UseCases.Tests*` trong `WORKTREE_PATH`.
2. Nếu không tìm thấy → báo user, skip P3.6 (không có test infra).

## Framework detection

Read `.csproj` PackageReference → adapt test pattern:

| Package | Test framework | Mock lib |
|---|---|---|
| `xunit` | xUnit (`[Fact]`, `[Theory]`, `[InlineData]`) | — |
| `NUnit` | NUnit (`[Test]`, `[TestCase]`) | — |
| `MSTest` | MSTest (`[TestMethod]`, `[DataRow]`) | — |
| `NSubstitute` | — | `Substitute.For<T>()`, `.When().Do()` |
| `Moq` | — | `Mock<T>()`, `.Setup()`, `.Verify()` |

Default fallback: xUnit + NSubstitute (project standard per session BLUF-2291).

## Mock identification

1. Read fix target file → constructor params (interfaces cần mock).
2. `grep` target class name trong `ServiceCollectionExtensions` → verify DI registration (`AddScoped<IInterface, Impl>()` tự resolve params).
3. List interfaces to mock → mock objects trong test.

## Test pattern

`[Theory]` + `[InlineData]` cover before/after behavior:
- Input đại diện cho bug condition (vd qa7 string) → expect default behavior (bug fixed).
- Input đại diện cho normal condition (vd Production) → expect production behavior.

Capture calls via mock lib (`NSubstitute.When().Do()` hoặc `Moq.Setup()`) → assert command/param truyền đúng.

## Build-test loop (max 3)

```
1. dotnet build (trong WORKTREE_PATH)
   - fail "assets file not found" → dotnet restore → retry
2. write test file (max 80 lines/edit)
3. dotnet test --filter <test-name>
   - fail CS0246 missing type → grep namespace → edit using → retry
   - fail CS1729 constructor → read target class → edit constructor call (vd `new T { Prop = x }` thay `new T(x)`) → retry
4. pass → output
5. max 3 iterations → escalate (báo user, không fix thêm)
```

## Output

```text
Test file: <path>
Framework: <xUnit+NSubstitute | ...>
Result: <N>/<N> passed
Evidence: <1 dòng tóm tắt — vd "qa7 string → default email; Production → production email">
```

Output feed vào P4.3 PR Evidence section.

## Constraints

- Không Python (project stack: .NET/TS/SQL).
- Không thêm dependency (dùng framework đã có trong test project).
- Max 80 lines/edit (project constraint).
- UTF-8 encoding.
- Chỉ tạo test cho public behavior đã fix, không test internals.
