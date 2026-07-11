// InspectionMappingProfile.cs
// AutoMapper 映射配置
// 作者：蘅芜君

using AutoMapper;
using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Application.Profiles;

/// <summary>
/// AutoMapper 映射配置
/// </summary>
public class InspectionMappingProfile : Profile
{
    public InspectionMappingProfile()
    {
        // InspectionResult -> InspectionResultDto
        CreateMap<InspectionResult, InspectionResultDto>()
            .ForMember(dest => dest.OutputImage, opt => opt.MapFrom(src =>
                src.OutputImage != null ? Convert.ToBase64String(src.OutputImage) : null))
            .ForMember(dest => dest.ExecutionOutcome, opt => opt.MapFrom(src => src.GetOutcome().Execution))
            .ForMember(dest => dest.DecisionOutcome, opt => opt.MapFrom(src => src.GetOutcome().Decision))
            .ForMember(dest => dest.HasJudgmentSignal, opt => opt.MapFrom(src => src.GetOutcome().HasJudgmentSignal))
            .ForMember(dest => dest.DecisionSource, opt => opt.MapFrom(src => src.GetOutcome().DecisionSource))
            .ForMember(dest => dest.ReasonCode, opt => opt.MapFrom(src => src.GetOutcome().ReasonCode))
            .ForMember(dest => dest.OutputData, opt => opt.MapFrom(src =>
                AnalysisPayloadSerialization.DeserializeJsonDictionary(src.OutputDataJson)))
            .ForMember(dest => dest.AnalysisData, opt => opt.MapFrom(src =>
                AnalysisPayloadSerialization.DeserializeAnalysisData(src.AnalysisDataJson)));

        // Defect -> DefectDto
        CreateMap<Defect, DefectDto>();
    }
}
