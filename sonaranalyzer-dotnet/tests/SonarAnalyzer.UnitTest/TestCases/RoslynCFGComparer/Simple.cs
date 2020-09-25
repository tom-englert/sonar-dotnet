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
        string value;
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
}
