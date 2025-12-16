namespace Faqt

open System.Runtime.CompilerServices
open Faqt.AssertionHelpers

type FaqtExtensions =
    /// Asserts that the assertion satisfies exactly one of the items in the subject list.
    [<Extension>]
    static member SatisfyExactlyOneThat(t: Testable<#seq<'a>>, assertion: 'a -> 'ignored, ?because) : And<_> =
        let stringOptimizedLength (xs: seq<'a>) =
            match box xs with
            | :? string as x -> x.Length
            | _ -> Seq.length xs

        use _ = t.Assert(true)

        if isNull (box t.Subject) then
            t.With("But was", t.Subject).Fail(because)

        let subjectLength = stringOptimizedLength t.Subject

        if subjectLength <= 0 then
            t
                .With("Minimum length", 1)
                .With("Actual length", subjectLength)
                .With("Subject value", t.Subject)
                .Fail(because)

        let failures = ResizeArray()
        let successes = ResizeArray()

        for i, a in Seq.indexed t.Subject do
            try
                assertion a |> ignore
                successes.Add(box {| Index = i; Item = a |})
            with
            | :? AssertionFailedException as ex -> failures.Add(box ex.FailureData)
            | ex -> failures.Add(box {| Exception = ex |})

        if successes.Count <> 1 then
            t
                .With("Required successes", 1)
                .With("Total successes", successes.Count)
                .With("Successes", successes)
                .With("Failures", failures)
                .Fail(because)

        And(t)
