using System;
using System.Reflection;

// Headless contract tests only: this does not create a Form, WebView or game session.
static class InputHostLifecycleTests
{
    static int passed;
    static int failed;

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static T Field<T>(object command, string name)
    {
        Assert(command != null, "Expected a host command.");
        PropertyInfo property = command.GetType().GetProperty(name);
        Assert(property != null, "Missing command property: " + name);
        return (T)property.GetValue(command, null);
    }

    static string NewDocument() { return Guid.NewGuid().ToString("D"); }

    static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error.Message);
        }
    }

    static void UnreadyCannotSend()
    {
        InputHostLifecycle state = new InputHostLifecycle();
        Assert(!state.Ready && !state.Closed, "New host should be open and unbound.");
        Assert(state.Command(true, "enable", true) == null, "Unbound host enabled input.");
        Assert(state.Command(false, "observe", false) == null, "Unbound host sent a command.");
        Assert(!state.Observe(NewDocument(), 0), "Unbound host accepted a page message.");
        Assert(state.Revision == 0, "Rejected commands consumed a revision.");
    }

    static void StaleNavigationCannotBind()
    {
        InputHostLifecycle state = new InputHostLifecycle();
        int oldGeneration = state.Invalidate();
        int currentGeneration = state.Invalidate();
        Assert(!state.Bind(oldGeneration, NewDocument()), "Late navigation completion was accepted.");
        Assert(!state.Ready, "Rejected navigation changed readiness.");
        string current = NewDocument();
        Assert(state.Bind(currentGeneration, current), "Current navigation did not bind.");
        Assert(!state.Bind(oldGeneration, NewDocument()), "Old navigation replaced a bound document.");
        Assert(state.DocumentId == current, "Old navigation changed document identity.");
    }

    static void InvalidDocumentCannotBind()
    {
        InputHostLifecycle state = new InputHostLifecycle();
        int generation = state.Invalidate();
        string[] invalid = { null, "", "not-a-guid", Guid.NewGuid().ToString("N"), "{" + NewDocument() + "}" };
        foreach (string document in invalid)
            Assert(!state.Bind(generation, document), "Invalid or noncanonical document ID was accepted.");
        Assert(!state.Ready, "Invalid document marked the host ready.");
        Assert(state.Bind(generation, NewDocument()), "Valid document ID was rejected.");
    }

    static void NewDocumentReceivesObserveCommand()
    {
        InputHostLifecycle state = new InputHostLifecycle();
        string document = NewDocument();
        Assert(state.Bind(state.Invalidate(), document), "Could not bind document.");
        object command = state.Command(false, "document-ready", false);
        Assert(Field<string>(command, "kind") == "mode", "Unexpected command kind.");
        Assert(Field<string>(command, "documentId") == document, "Command addressed another document.");
        Assert(!Field<bool>(command, "enabled"), "New document observation enabled input.");
        Assert(Field<string>(command, "reason") == "document-ready", "Command lost its reason.");
        Assert(Field<int>(command, "revision") == 1, "First command revision was not 1.");
        Assert(Field<int>(command, "cancellationEpoch") == 0, "New document inherited an old epoch.");
    }

    static void ForegroundRequiredOnlyForEnable()
    {
        InputHostLifecycle state = new InputHostLifecycle();
        state.Bind(state.Invalidate(), NewDocument());
        Assert(state.Command(true, "enable", false) == null, "Background host enabled input.");
        Assert(state.Revision == 0, "Rejected enable consumed a revision.");
        object enabled = state.Command(true, "enable", true);
        Assert(Field<bool>(enabled, "enabled"), "Foreground enable was rejected.");
        object cancelled = state.Command(false, "deactivate", false);
        Assert(!Field<bool>(cancelled, "enabled"), "Background cancellation was rejected.");
        Assert(Field<int>(cancelled, "revision") > Field<int>(enabled, "revision"), "Cancellation is not newer than enable.");
    }

    static void OldDocumentAndEpochAreRejected()
    {
        InputHostLifecycle state = new InputHostLifecycle();
        string oldDocument = NewDocument();
        state.Bind(state.Invalidate(), oldDocument);
        Assert(state.Observe(oldDocument, 4), "Current cancellation epoch was rejected.");
        Assert(!state.Observe(oldDocument, 3), "Earlier cancellation epoch was accepted.");
        Assert(!state.Observe(oldDocument, -1), "Negative cancellation epoch was accepted.");
        Assert(state.CancellationEpoch == 4, "Rejected epoch changed the current epoch.");
        Assert(state.Observe(oldDocument, 4), "Repeated current epoch should remain valid.");
        Assert(Field<int>(state.Command(true, "enable", true), "cancellationEpoch") == 4, "Command omitted current epoch.");

        state.Invalidate();
        Assert(!state.Observe(oldDocument, 5), "Navigating host accepted previous document input.");
        string currentDocument = NewDocument();
        state.Bind(state.Generation, currentDocument);
        Assert(!state.Observe(oldDocument, 100), "Old document advanced a new document's epoch.");
        Assert(state.CancellationEpoch == 0, "Old document contaminated new epoch.");
        Assert(state.Observe(currentDocument, 1), "New document input was rejected.");
    }

    static void DuplicateBindingCannotRollBackEpoch()
    {
        InputHostLifecycle state = new InputHostLifecycle();
        string document = NewDocument();
        int generation = state.Invalidate();
        state.Bind(generation, document);
        state.Observe(document, 6);
        // Either reject a duplicate handshake or accept it without changing the epoch.
        state.Bind(generation, document);
        Assert(state.DocumentId == document && state.CancellationEpoch == 6, "Duplicate binding reset cancellation history.");
        Assert(!state.Observe(document, 5), "Duplicate binding reaccepted a stale page message.");
        Assert(!state.Bind(generation, NewDocument()), "A second document replaced the current one without a navigation.");
        Assert(state.DocumentId == document && state.CancellationEpoch == 6, "Rejected replacement changed document state.");
    }

    static void ClosingIsTerminal()
    {
        InputHostLifecycle state = new InputHostLifecycle();
        string document = NewDocument();
        state.Bind(state.Invalidate(), document);
        state.Command(true, "enable", true);
        state.Close();
        int revision = state.Revision;
        Assert(state.Closed && !state.Ready, "Closing did not clear readiness.");
        Assert(!state.Bind(state.Generation, NewDocument()), "Closed host rebound a document.");
        Assert(!state.Observe(document, 7), "Closed host accepted input.");
        Assert(state.Command(true, "enable", true) == null, "Closed host sent enable.");
        Assert(state.Command(false, "cancel", false) == null, "Closed host sent cancellation.");
        state.Close();
        state.Invalidate();
        Assert(!state.Bind(state.Generation, NewDocument()), "Invalidation reopened the closed host.");
        Assert(state.Revision == revision, "Closed host advanced command revision.");
    }

    static void ConsecutiveReloadsRemainIsolated()
    {
        InputHostLifecycle state = new InputHostLifecycle();
        int previousGeneration = -1;
        int previousRevision = 0;
        string previousDocument = null;
        for (int index = 0; index < 24; index++)
        {
            int generation = state.Invalidate();
            Assert(generation > previousGeneration, "Reload did not advance navigation generation.");
            Assert(!state.Ready && state.Command(true, "enable", true) == null, "Reload did not suspend input.");
            if (previousDocument != null)
            {
                Assert(!state.Bind(previousGeneration, previousDocument), "Previous completion survived another reload.");
                Assert(!state.Observe(previousDocument, 1000), "Previous page survived another reload.");
            }
            string document = NewDocument();
            Assert(state.Bind(generation, document), "Current reload could not bind.");
            object observe = state.Command(false, "document-ready", false);
            Assert(Field<int>(observe, "revision") > previousRevision, "Command revision restarted after reload.");
            Assert(Field<int>(observe, "cancellationEpoch") == 0, "Reload retained an old cancellation epoch.");
            state.Observe(document, index + 1);
            previousRevision = Field<int>(state.Command(true, "enable", true), "revision");
            previousGeneration = generation;
            previousDocument = document;
        }
    }

    public static int Main()
    {
        Run("unready host cannot issue commands or accept messages", UnreadyCannotSend);
        Run("late navigation completion cannot replace the current document", StaleNavigationCannotBind);
        Run("invalid document IDs cannot bind", InvalidDocumentCannotBind);
        Run("new document receives an addressed observation command", NewDocumentReceivesObserveCommand);
        Run("foreground is required for enable but not cancellation", ForegroundRequiredOnlyForEnable);
        Run("stale document messages and cancellation epochs are rejected", OldDocumentAndEpochAreRejected);
        Run("duplicate binding cannot roll back cancellation history", DuplicateBindingCannotRollBackEpoch);
        Run("closing permanently rejects bindings messages and commands", ClosingIsTerminal);
        Run("consecutive reloads isolate documents and retain monotonic revisions", ConsecutiveReloadsRemainIsolated);
        Console.WriteLine("RESULT passed=" + passed + " failed=" + failed);
        return failed == 0 ? 0 : 1;
    }
}
