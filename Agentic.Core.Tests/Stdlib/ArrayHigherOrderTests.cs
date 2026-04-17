using Agentic.Core.Execution;
using FluentAssertions;
using Xunit;

namespace Agentic.Core.Tests.Stdlib;

public sealed class ArrayHigherOrderTests
{
    private static CompileResult Check(string source) =>
        new Compiler(emitBinary: false).Compile(source);

    [Fact]
    public void ArrLength_ShouldReturnSize()
    {
        var r = Check(@"(module T
            (test t (do
                (def a (arr.new 5))
                (assert-eq (arr.length a) 5))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void ArrMap_ShouldTransformEachElement()
    {
        var r = Check(@"(module T
            (defun double_it ((x : Num)) : Num (return (* x 2)))
            (test t (do
                (def a (arr.new 3))
                (arr.set a 0 1)
                (arr.set a 1 2)
                (arr.set a 2 3)
                (def b (arr.map a double_it))
                (assert-eq (arr.get b 0) 2)
                (assert-eq (arr.get b 1) 4)
                (assert-eq (arr.get b 2) 6))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void ArrFilter_ShouldKeepMatchingElements()
    {
        var r = Check(@"(module T
            (defun is_positive ((x : Num)) : Num (return (if (> x 0) 1 0)))
            (test t (do
                (def a (arr.new 4))
                (arr.set a 0 -1)
                (arr.set a 1 2)
                (arr.set a 2 -3)
                (arr.set a 3 4)
                (def b (arr.filter a is_positive))
                (assert-eq (arr.length b) 2)
                (assert-eq (arr.get b 0) 2)
                (assert-eq (arr.get b 1) 4))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void ArrReduce_ShouldFoldWithAccumulator()
    {
        var r = Check(@"(module T
            (defun add ((acc : Num) (x : Num)) : Num (return (+ acc x)))
            (test t (do
                (def a (arr.new 4))
                (arr.set a 0 1)
                (arr.set a 1 2)
                (arr.set a 2 3)
                (arr.set a 3 4)
                (assert-eq (arr.reduce a add 0) 10))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void ArrReduce_ShouldWorkWithMultiplication()
    {
        var r = Check(@"(module T
            (defun mul ((acc : Num) (x : Num)) : Num (return (* acc x)))
            (test t (do
                (def a (arr.new 3))
                (arr.set a 0 2)
                (arr.set a 1 3)
                (arr.set a 2 4)
                (assert-eq (arr.reduce a mul 1) 24))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void ArrMap_ChainedWithReduce()
    {
        var r = Check(@"(module T
            (defun square ((x : Num)) : Num (return (* x x)))
            (defun add ((acc : Num) (x : Num)) : Num (return (+ acc x)))
            (test t (do
                (def a (arr.new 3))
                (arr.set a 0 1)
                (arr.set a 1 2)
                (arr.set a 2 3)
                (def squared (arr.map a square))
                (assert-eq (arr.reduce squared add 0) 14))))");
        r.Success.Should().BeTrue();
    }

    [Fact]
    public void ArrLength_OnFilteredArray()
    {
        var r = Check(@"(module T
            (defun is_even ((x : Num)) : Num
                (return (if (= (- x (* 2 (/ (- x (- x (* 2 (/ x 2)))) 1))) 0) 1 0)))
            (defun gt_two ((x : Num)) : Num (return (if (> x 2) 1 0)))
            (test t (do
                (def a (arr.new 5))
                (arr.set a 0 1)
                (arr.set a 1 2)
                (arr.set a 2 3)
                (arr.set a 3 4)
                (arr.set a 4 5)
                (def filtered (arr.filter a gt_two))
                (assert-eq (arr.length filtered) 3))))");
        r.Success.Should().BeTrue();
    }
}
