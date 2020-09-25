using System;

public class Sample
{
    public void BinarySyntax()
    {
        var value = "Lorem" + "Ipsum" + "Dolor" + "Samet";
        Console.WriteLine(value);
    }

    public void Invocation(object arg)
    {
        var value = arg.ToString();
        Console.WriteLine(value);
    }

    public int ArrowAdd(int a, int b) => a + b;

    public void EmptyStatements()
    {
        ; ; ; ; ; ; ; ;
    }

    public void VariableDeclaration()
    {
        int a = 1, b = a + 1, c = 1 + 2 + 3;
    }

    public void Throw()
    {
        var a = "aaa";
        throw new Exception("Message");
        var b = "bbb";
    }

    public void VoidReturnBeforeExit()
    {
        var a = "aaa";
        return;
    }

    public void Index(string[] arr)
    {
        var value = arr[^1];
    }

    public void Range(string[] arr)
    {
        var value = arr[1..4];
    }
}

public class CallingBase : EmptyBase
{
    public CallingBase() : base(40 + 2)
    {
        var lorem = "Ipsum";
    }
}

public class EmptyBase
{
    public EmptyBase(int arg) { }
}
