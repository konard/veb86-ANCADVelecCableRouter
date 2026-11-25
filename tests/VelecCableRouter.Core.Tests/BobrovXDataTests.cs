using System;
using System.Linq;
using VelecCableRouter.Core.XData;
using Xunit;

namespace VelecCableRouter.Core.Tests
{
    public class BobrovXDataTests
    {
        [Fact]
        public void Parse_V2_ReadsTypedPairs()
        {
            var record = BobrovXData.Parse(new[]
            {
                XDataValue.App("OTHER_APP"), XDataValue.Str("noise"),
                XDataValue.App("BobrovXDATA"),
                XDataValue.Int16(2),
                XDataValue.Str("DEVICE_ID"), XDataValue.Str("3F2504E0-4F89-11D3-9A0C-0305E82C3301"),
                XDataValue.Str("TEMPLATE"), XDataValue.Str("DEVICE"),
                XDataValue.Str("NMO_Name"), XDataValue.Str("ИП-1.1"),
                XDataValue.Str("Elevation"), XDataValue.Real(3.5),
                XDataValue.Str("Poles"), XDataValue.Int32(3),
                XDataValue.Str("GroupName"), XDataValue.Int16(1),
                XDataValue.App("TAIL_APP"), XDataValue.Str("NMO_Name"), XDataValue.Str("wrong"),
            });

            Assert.Equal(BobrovXDataFormat.V2, record.Format);
            Assert.Empty(record.Warnings);
            Assert.Equal("3F2504E0-4F89-11D3-9A0C-0305E82C3301", record.DeviceId);
            Assert.True(record.IsTemplate("Device"));
            Assert.Equal("ИП-1.1", record.GetText("nmo_name"));
            Assert.Equal(3.5, record.GetDouble("Elevation"));
            Assert.Equal(3, record.GetDouble("Poles"));
            Assert.Equal("1", record.GetText("GroupName"));
            Assert.Equal(6, record.Parameters.Count); // DEVICE_ID, TEMPLATE and four parameters
        }

        [Fact]
        public void Parse_SectionWithoutAppHeader()
        {
            var record = BobrovXData.Parse(new[] { XDataValue.Int16(2), XDataValue.Str("TEMPLATE"), XDataValue.Str("SuperLine") });
            Assert.Equal(BobrovXDataFormat.V2, record.Format);
            Assert.True(record.IsTemplate("SuperLine"));
        }

        [Fact]
        public void Parse_NoSection_IsNone()
        {
            Assert.Equal(BobrovXDataFormat.None, BobrovXData.Parse(null).Format);
            Assert.Equal(BobrovXDataFormat.None, BobrovXData.Parse(new[] { XDataValue.App("OTHER"), XDataValue.Str("x") }).Format);
        }

        [Fact]
        public void Parse_V1_IsReportedAndNotRead()
        {
            var record = BobrovXData.Parse(new[] { XDataValue.App("BobrovXDATA"), XDataValue.Str("Name=ППК-1") });
            Assert.Equal(BobrovXDataFormat.V1, record.Format);
            Assert.Empty(record.Parameters);
            Assert.Single(record.Warnings);
        }

        [Fact]
        public void Parse_NewerVersion_ReadsWithWarning()
        {
            var record = BobrovXData.Parse(new[] { XDataValue.App("BobrovXDATA"), XDataValue.Int16(3), XDataValue.Str("NMO_Name"), XDataValue.Str("A") });
            Assert.Equal(BobrovXDataFormat.Newer, record.Format);
            Assert.Equal("A", record.GetText("NMO_Name"));
            Assert.Single(record.Warnings);
        }

        [Fact]
        public void Parse_SkipsTablesAndReportsDamage()
        {
            var record = BobrovXData.Parse(new[]
            {
                XDataValue.App("BobrovXDATA"), XDataValue.Int16(2),
                XDataValue.Str("Materials"), XDataValue.Control("{"),
                XDataValue.Control("{"), XDataValue.Str("Mat_Mark"), XDataValue.Str("CD35"), XDataValue.Control("}"),
                XDataValue.Control("}"),
                XDataValue.Str("NMO_Name"), XDataValue.Str("A"),
                XDataValue.Real(1.0),
                XDataValue.Str("NMO_Name"), XDataValue.Str("B"),
                XDataValue.Str("Tail"),
            });

            Assert.Equal("A", record.GetText("NMO_Name"));
            Assert.True(record.TryGet("Materials", out var table));
            Assert.Equal(DxfCode.ControlString, table.TypeCode);
            Assert.False(record.TryGet("Mat_Mark", out _));
            Assert.Equal(3, record.Warnings.Count); // orphan value, duplicate Id, Id without value
        }

        [Fact]
        public void BuildCable_WritesV2Contract()
        {
            var id = Guid.NewGuid();
            var values = BobrovXData.BuildCable("ОПС", "ППК-1", "ШС1", id);

            Assert.Equal(XDataValue.App("BobrovXDATA"), values[0]);
            Assert.Equal(XDataValue.Int16(2), values[1]);
            var record = BobrovXData.Parse(values);
            Assert.Equal(BobrovXDataFormat.V2, record.Format);
            Assert.Empty(record.Warnings);
            Assert.Equal(id.ToString("D"), record.DeviceId);
            Assert.Equal("Cable", record.Template);
            Assert.Equal("ОПС", record.GetText("NMO_Name"));
            Assert.Equal("ОПС", record.GetText("SuperLineName"));
            Assert.Equal("ППК-1", record.GetText("HeadDeviceName"));
            Assert.Equal("ШС1", record.GetText("GroupName"));
            Assert.All(values.Skip(2), v => Assert.Equal(DxfCode.String, v.TypeCode));
            Assert.Equal("DEVICE_ID", values[2].Value);
            Assert.Equal("TEMPLATE", values[4].Value);
        }

        [Fact]
        public void BuildCable_RejectsStringsLongerThan255Utf8Bytes()
        {
            var longName = new string('Ж', 128); // 256 bytes in UTF-8
            Assert.Throws<ArgumentException>(() => BobrovXData.BuildCable("ОПС", longName, "ШС1", Guid.NewGuid()));
            Assert.NotNull(BobrovXData.Validate(BobrovXData.BuildCableUnchecked("ОПС", longName, "ШС1", Guid.Empty)));
            Assert.Null(BobrovXData.Validate(BobrovXData.BuildCableUnchecked("ОПС", new string('Ж', 127), "ШС1", Guid.Empty)));
        }

        [Fact]
        public void XDataValue_ConvertsNumbersAndStrings()
        {
            Assert.Equal(2.5, XDataValue.Str("2,5").AsDouble());
            Assert.Null(XDataValue.Str("abc").AsDouble());
            Assert.Equal("2.5", XDataValue.Real(2.5).AsString());
            Assert.Equal(7, XDataValue.Int32(7).AsDouble());
        }
    }
}
