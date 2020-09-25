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

    public string Condition(bool condition)
    {
        string value = "Init";
        if (condition)
        {
            value = "True";
        }
        else
        {
            value = "False";
        }
        return value;
    }

    public string ElseIf(int value)
    {
        if (value == 0)
            return "Zero";
        else if (value == 1)
            return "One";
        else if (value == 2)
            return "Two";
        else
            return "Something else";
    }
}
