# Copilot Instructions

## General Guidelines
- When handling language codes, take only the first 2 characters (substring) to handle formats like "en-US", "az-AZ", "ar-SA" and normalize them to base language codes ("en", "az", "ar"). This is more secure and reliable than direct string comparison.