using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;

namespace ClearVision.Product.Application.Analysis;

public interface IAnalysisCardMapper
{
    bool CanMap(OperatorType operatorType);

    IEnumerable<AnalysisCardDto> Map(Operator @operator, OperatorExecutionResult result);
}

public class AnalysisCardRegistry
{
    private readonly IReadOnlyList<IAnalysisCardMapper> _mappers;

    public AnalysisCardRegistry(IEnumerable<IAnalysisCardMapper> mappers)
    {
        _mappers = mappers.ToList();
    }

    public IReadOnlyList<AnalysisCardDto> Map(Operator @operator, OperatorExecutionResult result)
    {
        var cards = new List<AnalysisCardDto>();

        foreach (var mapper in _mappers)
        {
            if (!mapper.CanMap(@operator.Type))
            {
                continue;
            }

            cards.AddRange(mapper.Map(@operator, result));
        }

        return cards;
    }
}
