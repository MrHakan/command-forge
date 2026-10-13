using System.Text;

namespace UniversalEditor;

// Extract values verbatim, preserving byte/short/long suffixes and typed arrays in unedited components.
internal static class SnbtSlices
{
    public static Dictionary<string,string> Compound(string text)
    {
        var values = new Dictionary<string,string>(); int i=0;
        void Space(){while(i<text.Length&&char.IsWhiteSpace(text[i]))i++;}
        Space();if(i>=text.Length||text[i++]!='{')throw new InvalidDataException("Expected an item compound.");
        while(true)
        {
            Space();if(i<text.Length&&text[i]=='}'){i++;break;}
            if(i>=text.Length)throw new InvalidDataException("Incomplete item compound.");
            string key;
            if(text[i] is '\'' or '"')key=Quoted(text,ref i);else{int start=i;while(i<text.Length&&text[i]!=':'&&!char.IsWhiteSpace(text[i]))i++;key=text[start..i];}
            Space();if(i>=text.Length||text[i++]!=':')throw new InvalidDataException("Invalid item component key.");Space();int valueStart=i;
            var stack=new Stack<char>();bool done=false;
            while(i<text.Length)
            {
                char c=text[i];if(c is '\'' or '"'){Quoted(text,ref i);continue;}
                if(c is '{' or '[')stack.Push(c);
                else if(c is '}' or ']')
                {if(stack.Count==0){done=true;break;}char opening=stack.Pop();if((c=='}'&&opening!='{')||(c==']'&&opening!='['))throw new InvalidDataException("Unbalanced item component.");}
                else if(c==','&&stack.Count==0){done=true;break;}i++;
            }
            if(!done||i==valueStart)throw new InvalidDataException("Incomplete item component.");
            if(!values.TryAdd(key,text[valueStart..i].Trim()))throw new InvalidDataException("Duplicate item component.");
            if(text[i]==','){i++;continue;}if(text[i]=='}'){i++;break;}throw new InvalidDataException("Invalid compound separator.");
        }
        Space();if(i!=text.Length)throw new InvalidDataException("Trailing item component data.");return values;
    }
    private static string Quoted(string text,ref int i)
    {
        char quote=text[i++];var key=new StringBuilder();while(i<text.Length){char c=text[i++];if(c==quote)return key.ToString();if(c=='\\'){if(i>=text.Length)break;c=text[i++];}key.Append(c);}throw new InvalidDataException("Incomplete item string.");
    }
}
