﻿using ReportService.Interfaces.Core;

namespace ReportService.Entities.Dto
{
    public class DtoOperTemplate : IDtoEntity
    {
        public int Id { get; set; }
        public string ImplementationType { get; set; }
        public string Name { get; set; }
        public string ConfigTemplate { get; set; }
    }
}