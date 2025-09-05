# A2A Analyzers

This project provides internal Roslyn analyzers for the A2A codebase.

Rules
- A2A0001: TypeMapping must start with null in BaseKindDiscriminatorConverter overrides.
- A2A0002: Discriminator enums must start with Unknown = 0.
- A2A0002: Discriminator enums must end with Count sentinel.
