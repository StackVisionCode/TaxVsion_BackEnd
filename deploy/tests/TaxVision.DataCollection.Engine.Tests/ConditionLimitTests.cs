using System.Text.Json;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class ConditionLimitTests
{
    [Fact]
    public void Rejects_more_than_fifty_condition_nodes()
    {
        var leaf = new Condition { Field = "fd_toggle000", Op = "isTrue" };
        var branches = Enumerable.Range(0, 10)
            .Select(_ => new Condition { All = Enumerable.Repeat(leaf, 5).ToArray() })
            .ToArray();
        var definition = EngineTestFactory.WithComponents(
            new FormComponent
            {
                Id = "fd_toggle000",
                Type = "yesNo",
                Label = "Toggle",
                Props = JsonSerializer.SerializeToElement(new { }),
            },
            new FormComponent
            {
                Id = "fd_detail000",
                Type = "text",
                Label = "Details",
                VisibleWhen = new Condition { All = branches },
                Props = JsonSerializer.SerializeToElement(new { }),
            }
        );

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, item => item.Code == "condition.too_many_nodes");
    }
}
