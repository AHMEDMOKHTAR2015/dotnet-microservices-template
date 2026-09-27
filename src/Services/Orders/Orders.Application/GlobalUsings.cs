// Third-party libraries
global using MediatR;
global using Mapster;
global using FluentValidation;

// Internal libraries
global using Blocks.Core;
global using Blocks.MediatR;
global using Blocks.EntityFrameworkCore;
global using Blocks.FluentValidation;
global using Starter.Abstractions;
global using Starter.Abstractions.Enums;

// Domain
global using Orders.Domain.Orders;
global using Orders.Domain.Shared;
global using Orders.Domain.Shared.Enums;
global using Orders.Domain.Shared.ValueObjects;
global using Orders.Domain.StateMachines;

// Application
global using Orders.Application.Features.Shared;

// Persistence
global using Orders.Persistence;
global using Orders.Persistence.Repositories;
