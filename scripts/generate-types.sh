#!/bin/bash
dotnet build ClearVision.Product/src/ClearVision.Product.Application/ClearVision.Product.Application.csproj
dotnet tool run dotnet-typegen generate
