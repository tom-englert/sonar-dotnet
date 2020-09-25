using System;

public class Sample
{
    public void ConstantAndNull(object a, object b)
    {
        var ret = a is 10 && b is null;
    }

    public void IsType(object a)
    {
        var ret = a is string str && str.Length > 0;
    }

    public void IsTypeInConditionChain(object a)
    {
        if(a is string str
            && str.GetType() is { } type
            && type.BaseType is Type baseType
            && baseType.IsAbstract)
        {
            var ret = "value";
        }
    }
}
