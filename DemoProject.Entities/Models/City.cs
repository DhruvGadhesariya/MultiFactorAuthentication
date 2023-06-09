﻿using System;
using System.Collections.Generic;

namespace DemoProject.Entities.Models;

public partial class City
{
    public long CityId { get; set; }

    public long CountryId { get; set; }

    public string Name { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? StandardTime { get; set; }

    public virtual ICollection<AvailableProduct> AvailableProducts { get; set; } = new List<AvailableProduct>();

    public virtual Country Country { get; set; } = null!;

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
