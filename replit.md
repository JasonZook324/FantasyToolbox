# FantasyToolbox

## Overview

FantasyToolbox is an ASP.NET Core web application designed to provide fantasy sports management tools. The application integrates with ESPN's fantasy sports API to help users manage their fantasy teams and access relevant data. Built on .NET 9.0, it uses Entity Framework Core for data persistence and includes user authentication capabilities through ASP.NET Core Identity.

## User Preferences

Preferred communication style: Simple, everyday language.

## System Architecture

### Web Framework
- **ASP.NET Core 9.0** with Razor Pages architecture
- Pages-based routing for simplified web development
- Built-in dependency injection container for service management

### Authentication & Authorization
- **ASP.NET Core Identity** for user management
- Entity Framework Core integration for identity data storage
- Session-based authentication for ESPN API integration

### Data Layer
- **Entity Framework Core** as the Object-Relational Mapper (ORM)
- **PostgreSQL** database hosted on Neon.tech cloud platform
- Code-first approach with migrations for database schema management
- Connection string configured for SSL-required cloud database

### Frontend
- **Razor Pages** for server-side rendered HTML
- **Bootstrap CSS framework** for responsive UI components
- Custom CSS styling with scoped component styles
- Client-side JavaScript for enhanced user interactions

### Service Layer
- Service-oriented architecture with dependency injection
- ESPN API integration service for fantasy sports data
- Session helper utilities for managing ESPN authentication state

### Configuration Management
- Environment-specific configuration files (Development/Production)
- Secure connection string management
- Centralized logging configuration

## External Dependencies

### Database
- **Neon.tech PostgreSQL** - Cloud-hosted PostgreSQL database with SSL encryption
- **Npgsql** - .NET PostgreSQL driver for database connectivity
- **Npgsql.EntityFrameworkCore.PostgreSQL** - PostgreSQL provider for Entity Framework Core

### Third-Party APIs
- **ESPN Fantasy Sports API** - Integration for retrieving fantasy league data, player statistics, and team information

### Authentication Services
- **Microsoft.AspNetCore.Identity.EntityFrameworkCore** - Identity framework with Entity Framework integration
- **Azure.Identity** and **Azure.Core** - Azure authentication libraries for potential cloud service integration

### Frontend Libraries
- **Bootstrap 5** - CSS framework for responsive design and UI components
- **jQuery** - JavaScript library for DOM manipulation and AJAX requests
- **jQuery Validation** - Client-side form validation library

### Development Tools
- **Microsoft.EntityFrameworkCore.Design** - Design-time tools for Entity Framework migrations
- **Newtonsoft.Json** - JSON serialization library for API data handling

### Cloud Services
- **Neon.tech** - PostgreSQL database hosting with connection pooling and SSL security