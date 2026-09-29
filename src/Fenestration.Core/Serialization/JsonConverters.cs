using System.Text.Json;
using System.Text.Json.Serialization;
using Fenestration.Core.Geometry;

namespace Fenestration.Core.Serialization;

/// <summary>
/// Custom JSON converter for <see cref="Point2D"/> to produce compact {"x":0,"y":0} output.
/// </summary>
public class Point2DJsonConverter : JsonConverter<Point2D>
{
    public override Point2D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double x = 0, y = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string prop = reader.GetString()!;
                reader.Read();
                if (prop.Equals("x", StringComparison.OrdinalIgnoreCase)) x = reader.GetDouble();
                else if (prop.Equals("y", StringComparison.OrdinalIgnoreCase)) y = reader.GetDouble();
                else reader.Skip(); // unknown property (possibly a nested object) — ignore
            }
        }
        return new Point2D(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Point2D value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}

/// <summary>
/// Custom JSON converter for <see cref="Rectangle2D"/>.
/// </summary>
public class Rectangle2DJsonConverter : JsonConverter<Rectangle2D>
{
    public override Rectangle2D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double x = 0, y = 0, w = 0, h = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string prop = reader.GetString()!;
                reader.Read();
                switch (prop.ToLowerInvariant())
                {
                    case "x": x = reader.GetDouble(); break;
                    case "y": y = reader.GetDouble(); break;
                    case "width": w = reader.GetDouble(); break;
                    case "height": h = reader.GetDouble(); break;
                    default: reader.Skip(); break;
                }
            }
        }
        return new Rectangle2D(x, y, w, h);
    }

    public override void Write(Utf8JsonWriter writer, Rectangle2D value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("width", value.Width);
        writer.WriteNumber("height", value.Height);
        writer.WriteEndObject();
    }
}
