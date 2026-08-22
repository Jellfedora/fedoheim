CREATE TABLE `faq_entries` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`question` text NOT NULL,
	`answer` text NOT NULL,
	`sort_order` integer DEFAULT 0 NOT NULL
);
--> statement-breakpoint
CREATE TABLE `rules` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`text` text NOT NULL,
	`sort_order` integer DEFAULT 0 NOT NULL
);
--> statement-breakpoint
ALTER TABLE `mods` ADD `description` text DEFAULT '' NOT NULL;--> statement-breakpoint
ALTER TABLE `mods` ADD `category` text DEFAULT 'Gameplay' NOT NULL;