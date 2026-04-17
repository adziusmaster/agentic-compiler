# App Ideas for Agentic Compiler
#
# Use these as starting points with:
#   dotnet run -- agent "<description>" --out <Name>
#
# Each entry is a CLI command you can copy-paste.

# ─── Simple Functions ───────────────────────────────────
# Basic math utility
dotnet run -- agent "Build a function that converts Celsius to Fahrenheit and back" --out TempConverter

# String processing
dotnet run -- agent "Build a function that counts vowels in a string" --out VowelCounter

# ─── Data Processing ────────────────────────────────────
# Statistics
dotnet run -- agent "Build a statistics module with mean, variance, and standard deviation for 5 numbers" --out Stats

# Grade book
dotnet run -- agent "Build a grade book with add_grade, compute_gpa, highest and lowest for 6 grades" --out GradeBook

# Sorting
dotnet run -- agent "Build a bubble sort function for an array of 10 numbers" --out BubbleSort

# ─── Multi-File Projects ────────────────────────────────
# Create these as .ag files and compile with: dotnet run -- compile main.ag
#
# Example: math_lib.ag + main.ag (see samples/ directory)
# The import syntax is: (import "./other_file.ag")
# Control visibility with: (export func1 func2)

# ─── HTTP Servers ────────────────────────────────────────
# Simple API (requires --allow-http)
dotnet run -- agent "Build an API with GET /greet/:name that returns a greeting" --out GreetApi

# REST-style endpoint
dotnet run -- agent "Build an API with GET /stats that returns mean and count from stored numbers" --out StatsApi

# ─── More Complex ────────────────────────────────────────
# Text analyzer
dotnet run -- agent "Build a text analyzer that counts words, characters, and sentences from input" --out TextAnalyzer

# Matrix operations
dotnet run -- agent "Build functions for matrix addition and scalar multiplication using flat arrays" --out Matrix

# Fibonacci
dotnet run -- agent "Build a function that computes the Nth Fibonacci number using a while loop" --out Fibonacci
