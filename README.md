# SkillMax

A study app that turns your PDF books into a scrolling feed: one page at a time, mixed with YouTube Shorts on the same subjects.

## Features
- **Books as a feed**: import a PDF and its pages come back as posts in a random feed, so studying feels like scrolling.
- **Understood pages**: mark a page as understood and it stays in rotation for revision, but comes up less often than pages that haven't landed yet.
- **Saved pages**: bookmark a page to resurface it later.
- **YouTube Shorts**: follow channels or searches and their Shorts appear between book pages, plus a stories rail with one bubble per channel. Uses YouTube's public pages and feeds, no API key.
- **Categories**: one set of labels for both books and channels, and the feed can be filtered to one category.
- **Reader**: read a book straight through, with chapters taken from the PDF's own outline.
- **Ask an AI**: send a page to your chat app with a prompt you write once in settings.

## Stack
- .NET 9, MAUI Blazor Hybrid (Android + Windows)
- EF Core 9 + SQLite (local-first, single user)
- PdfPig for page text, PDFtoImage for page rendering

## Solution layout
- `src/SkillMax.Domain`: entities (books, channels, categories, saved and understood pages)
- `src/SkillMax.Application`: service abstractions and models
- `src/SkillMax.Infrastructure`: EF Core, SQLite, PDF import, feed and YouTube services
- `src/SkillMax.Maui`: MAUI Blazor Hybrid host

## Running it
Requires the .NET 9 SDK with the .NET MAUI workload. Open `SkillMax.sln` and start `SkillMax.Maui` on Windows or Android.
