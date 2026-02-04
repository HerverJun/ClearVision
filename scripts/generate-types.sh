#!/bin/bash
dotnet build Acme.Product/src/Acme.Product.Application/Acme.Product.Application.csproj
dotnet tool run dotnet-typegen generate
