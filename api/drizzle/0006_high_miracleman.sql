ALTER TABLE `announcements` ADD `title` text;--> statement-breakpoint
ALTER TABLE `announcements` ADD `images` text DEFAULT '[]' NOT NULL;--> statement-breakpoint
ALTER TABLE `announcements` ADD `updated_at` integer;